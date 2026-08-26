using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

[Collection(nameof(DatabaseCollection))]
public class ReceivablesTests
{
    private static readonly DateOnly August = new(2026, 8, 15);

    private sealed record Kit(
        IReceivablesService Receivables,
        ISalesInvoiceService Invoices,
        IPostingService Postings,
        AccountingDbContext Db) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static Kit KitFor(LedgerWorld world)
    {
        var db = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(db);
        var postings = new PostingService(db, user, numbers, NullLogger<PostingService>.Instance);
        var invoices = new SalesInvoiceService(
            db, user, numbers, postings, new SalesInvoicePostingRule(),
            NullLogger<SalesInvoiceService>.Instance);
        var receivables = new ReceivablesService(
            db, user, numbers, postings, NullLogger<ReceivablesService>.Instance);
        return new Kit(receivables, invoices, postings, db);
    }

    private static async Task<SalesInvoiceDetail> PostedInvoiceAsync(
        Kit kit, LedgerWorld world, decimal amount, string? currency = null, decimal? rate = null)
    {
        var draft = await kit.Invoices.CreateDraftAsync(new CreateSalesInvoiceRequest(
            world.EntityId, world.CustomerId, August,
            [new CreateSalesInvoiceLineRequest("Advisory", 1m, amount, world.SalesAccountId)],
            CurrencyCode: currency, FxRate: rate));

        return await kit.Invoices.PostAsync(draft.Id);
    }

    private static async Task<ReceiptSummary> PostedReceiptAsync(
        Kit kit, LedgerWorld world, decimal amount, string? currency = null, decimal? rate = null)
    {
        var receipt = await kit.Receivables.CreateReceiptAsync(new CreateReceiptRequest(
            world.EntityId, world.CustomerId, world.CashAccountId, August, amount,
            CurrencyCode: currency, FxRate: rate));

        return await kit.Receivables.PostReceiptAsync(receipt.Id);
    }

    // ---------------------------------------------------------------- receipts

    [Fact]
    public async Task Receipt_PostsDebitBankCreditReceivablesWithTheCustomer()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var receipt = await PostedReceiptAsync(kit, world, 1000m);

        Assert.Equal("Posted", receipt.State);
        Assert.Equal("OR-2026-00001", receipt.DocNo);

        var entry = await kit.Postings.GetAsync(receipt.JournalEntryId!.Value);

        var bank = entry.Lines.Single(l => l.AccountId == world.CashAccountId);
        var receivable = entry.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);

        Assert.Equal("Debit", bank.Direction);
        Assert.Equal("Credit", receivable.Direction);
        Assert.Equal(world.CustomerId, receivable.CustomerId);
    }

    [Fact]
    public async Task Receipt_IntoANonBankAccount_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.CreateReceiptAsync(new CreateReceiptRequest(
                world.EntityId, world.CustomerId, world.SalesAccountId, August, 100m)));

        Assert.Contains("not a bank or cash account", ex.Message);
    }

    [Fact]
    public async Task Receipt_ForANonPositiveAmount_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.CreateReceiptAsync(new CreateReceiptRequest(
                world.EntityId, world.CustomerId, world.CashAccountId, August, 0m)));
    }

    [Fact]
    public async Task PostedReceipt_CannotBeChanged()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var receipt = await PostedReceiptAsync(kit, world, 500m);

        await using var editor = world.NewAppContext();
        var row = await editor.CustomerReceipts.FirstAsync(r => r.Id == receipt.Id);
        row.Memo = "tampered";

        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("posted and cannot be changed", ex!.GetBaseException().Message);
    }

    // ---------------------------------------------------------------- allocation

    [Fact]
    public async Task Allocation_ReducesWhatTheInvoiceHasOutstanding()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 1000m);
        var receipt = await PostedReceiptAsync(kit, world, 400m);

        await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 400m)]));

        var open = await kit.Receivables.GetOpenInvoicesAsync(world.EntityId, null);
        var row = open.Single(i => i.Id == invoice.Id);

        Assert.Equal(400m, row.Allocated);
        Assert.Equal(600m, row.Outstanding);
    }

    [Fact]
    public async Task Allocation_ThatFullySettlesAnInvoice_RemovesItFromOpenItems()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 250m);
        var receipt = await PostedReceiptAsync(kit, world, 250m);

        await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 250m)]));

        var open = await kit.Receivables.GetOpenInvoicesAsync(world.EntityId, null);
        Assert.DoesNotContain(open, i => i.Id == invoice.Id);
    }

    [Fact]
    public async Task Allocating_MoreThanWasReceived_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 1000m);
        var receipt = await PostedReceiptAsync(kit, world, 100m);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.AllocateAsync(
                new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 500m)])));

        Assert.Contains("would invent money", ex.Message);
    }

    [Fact]
    public async Task Allocating_MoreThanTheInvoiceOwes_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m);
        var receipt = await PostedReceiptAsync(kit, world, 500m);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.AllocateAsync(
                new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 300m)])));

        Assert.Contains("outstanding", ex.Message);
    }

    [Fact]
    public async Task Allocating_ADraftReceipt_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m);
        var draft = await kit.Receivables.CreateReceiptAsync(new CreateReceiptRequest(
            world.EntityId, world.CustomerId, world.CashAccountId, August, 100m));

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.AllocateAsync(
                new AllocateRequest(draft.Id, [new AllocationLineRequest(invoice.Id, 100m)])));

        Assert.Contains("not in the books yet", ex.Message);
    }

    [Fact]
    public async Task Allocating_ToAnotherCustomersInvoice_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var otherCustomerId = Guid.NewGuid();
        await using (var setup = world.NewAppContext())
        {
            setup.Customers.Add(new Customer
            {
                Id = otherCustomerId,
                TenantId = world.TenantId,
                Code = "C0002",
                Name = "Someone Else",
                CurrencyCode = "MYR",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var theirDraft = await kit.Invoices.CreateDraftAsync(new CreateSalesInvoiceRequest(
            world.EntityId, otherCustomerId, August,
            [new CreateSalesInvoiceLineRequest("Advisory", 1m, 100m, world.SalesAccountId)]));
        var theirInvoice = await kit.Invoices.PostAsync(theirDraft.Id);

        var receipt = await PostedReceiptAsync(kit, world, 100m);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.AllocateAsync(
                new AllocateRequest(receipt.Id, [new AllocationLineRequest(theirInvoice.Id, 100m)])));

        Assert.Contains("different customer", ex.Message);
    }

    [Fact]
    public async Task Unallocate_InsertsAReversingRowAndRestoresTheOutstanding()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 800m);
        var receipt = await PostedReceiptAsync(kit, world, 800m);

        var allocated = await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 800m)]));

        var reversal = await kit.Receivables.UnallocateAsync(allocated[0].Id);

        Assert.Equal(allocated[0].Id, reversal.ReversesAllocationId);
        Assert.Equal(-800m, reversal.Amount);

        // Nothing was deleted — both rows stand, and they net to nothing.
        await using var reader = world.NewAppContext();
        Assert.Equal(2, await reader.Allocations.CountAsync());
        Assert.Equal(0m, await reader.Allocations.SumAsync(a => a.Amount));

        var open = await kit.Receivables.GetOpenInvoicesAsync(world.EntityId, null);
        Assert.Equal(800m, open.Single(i => i.Id == invoice.Id).Outstanding);
    }

    [Fact]
    public async Task Unallocate_Twice_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m);
        var receipt = await PostedReceiptAsync(kit, world, 100m);
        var allocated = await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)]));

        await kit.Receivables.UnallocateAsync(allocated[0].Id);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.UnallocateAsync(allocated[0].Id));
    }

    [Fact]
    public async Task ApplicationRole_CannotUpdateOrDeleteAnAllocation()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m);
        var receipt = await PostedReceiptAsync(kit, world, 100m);
        await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)]));

        await using var editor = world.NewAppContext();
        var row = await editor.Allocations.FirstAsync();
        row.Amount = 99m;

        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("permission denied", ex!.GetBaseException().Message);
    }

    // ---------------------------------------------------------------- realised FX

    [Fact]
    public async Task SettlingAtADifferentRate_PostsTheExchangeDifferenceAndClearsTheCustomer()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        // Invoiced 100 USD when a dollar was worth 4.70; paid when it was worth 4.50.
        // The receivable was carried at 470 and only 450 was realised — a 20 loss.
        var invoice = await PostedInvoiceAsync(kit, world, 100m, "USD", 4.7m);
        var receipt = await PostedReceiptAsync(kit, world, 100m, "USD", 4.5m);

        var allocations = await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)]));

        Assert.Equal(20m, allocations[0].FxGainLossFunctional);
        Assert.NotNull(allocations[0].JournalEntryId);

        // The point of the exercise: the customer's balance goes to zero. Without the
        // exchange entry, 20 would sit on the control account forever.
        var ageing = await kit.Receivables.GetAgeingAsync(world.EntityId, new DateOnly(2026, 12, 31));
        Assert.Empty(ageing.Customers);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        var receivables = trialBalance.Lines.SingleOrDefault(l => l.AccountId == world.ReceivablesAccountId);
        Assert.Equal(0m, receivables?.Balance ?? 0m);

        // The loss landed on the realised FX account, as a debit.
        var fx = trialBalance.Lines.Single(l => l.AccountId == world.FxAccountId);
        Assert.Equal(20m, fx.Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task SettlingAtAFavourableRate_PostsAGain()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        // Invoiced at 4.50, paid when the dollar was worth 4.70 — 20 more than carried.
        var invoice = await PostedInvoiceAsync(kit, world, 100m, "USD", 4.5m);
        var receipt = await PostedReceiptAsync(kit, world, 100m, "USD", 4.7m);

        var allocations = await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)]));

        Assert.Equal(-20m, allocations[0].FxGainLossFunctional);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        var fx = trialBalance.Lines.Single(l => l.AccountId == world.FxAccountId);

        // A gain is a credit, so the balance is negative in debit-positive terms.
        Assert.Equal(-20m, fx.Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task SettlingAtTheSameRate_PostsNoExchangeEntry()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m, "USD", 4.5m);
        var receipt = await PostedReceiptAsync(kit, world, 100m, "USD", 4.5m);

        var allocations = await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)]));

        Assert.Equal(0m, allocations[0].FxGainLossFunctional);
        Assert.Null(allocations[0].JournalEntryId);
    }

    [Fact]
    public async Task Allocating_AcrossCurrencies_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 100m, "USD", 4.5m);
        var receipt = await PostedReceiptAsync(kit, world, 450m);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Receivables.AllocateAsync(
                new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 100m)])));

        Assert.Contains("Cross-currency", ex.Message);
    }

    // ---------------------------------------------------------------- reporting

    /// <summary>
    /// The claim the whole architecture was chosen for.
    /// </summary>
    [Fact]
    public async Task AgeingTotal_AlwaysEqualsTheReceivablesControlAccount()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var first = await PostedInvoiceAsync(kit, world, 1000m);
        var second = await PostedInvoiceAsync(kit, world, 250m);
        var receipt = await PostedReceiptAsync(kit, world, 400m);

        await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(first.Id, 400m)]));

        var asOf = new DateOnly(2026, 12, 31);
        var ageing = await kit.Receivables.GetAgeingAsync(world.EntityId, asOf);
        var trialBalance = await kit.Postings.GetTrialBalanceAsync(world.EntityId, asOf);

        var controlAccount = trialBalance.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);

        // 1250 invoiced, 400 received. Both numbers come from the same postings, so a
        // difference here would be a defect, not a reconciliation task.
        Assert.Equal(850m, ageing.Total);
        Assert.Equal(ageing.Total, controlAccount.Balance);
    }

    [Fact]
    public async Task Ageing_PutsAnOverdueInvoiceInTheRightBucket()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        // Invoice dated 15 August with 30-day terms is due 14 September.
        await PostedInvoiceAsync(kit, world, 100m);

        var justDue = await kit.Receivables.GetAgeingAsync(world.EntityId, new DateOnly(2026, 9, 14));
        Assert.Equal(100m, justDue.Customers.Single().Current);

        var fortyDaysLate = await kit.Receivables.GetAgeingAsync(world.EntityId, new DateOnly(2026, 10, 24));
        Assert.Equal(100m, fortyDaysLate.Customers.Single().Days31To60);
    }

    [Fact]
    public async Task Statement_ShowsEveryMovementWithARunningBalance()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        var invoice = await PostedInvoiceAsync(kit, world, 1000m);
        var receipt = await PostedReceiptAsync(kit, world, 300m);
        await kit.Receivables.AllocateAsync(
            new AllocateRequest(receipt.Id, [new AllocationLineRequest(invoice.Id, 300m)]));

        var statement = await kit.Receivables.GetStatementAsync(
            world.EntityId, world.CustomerId, new DateOnly(2026, 12, 31));

        Assert.Equal(2, statement.Lines.Count);
        Assert.Equal(1000m, statement.Lines[0].Debit);
        Assert.Equal(300m, statement.Lines[1].Credit);
        Assert.Equal(700m, statement.ClosingBalance);
    }

    [Fact]
    public async Task Statement_ForACustomerThatDoesNotExist_IsNotFound()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await Assert.ThrowsAsync<NotFoundException>(
            () => kit.Receivables.GetStatementAsync(
                world.EntityId, Guid.NewGuid(), new DateOnly(2026, 12, 31)));
    }
}
