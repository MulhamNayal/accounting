using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

[Collection(nameof(DatabaseCollection))]
public class SalesInvoiceTests
{
    private static readonly DateOnly August = new(2026, 8, 15);

    private static (ISalesInvoiceService Invoices, IPostingService Postings, AccountingDbContext Db)
        ServicesFor(LedgerWorld world)
    {
        var db = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(db);
        var postings = new PostingService(db, user, numbers, NullLogger<PostingService>.Instance);
        var invoices = new SalesInvoiceService(
            db, user, numbers, postings, new SalesInvoicePostingRule(),
            NullLogger<SalesInvoiceService>.Instance);
        return (invoices, postings, db);
    }

    private static CreateSalesInvoiceRequest Request(LedgerWorld world, params (string Text, decimal Qty, decimal Price)[] lines)
        => new(
            world.EntityId,
            world.CustomerId,
            August,
            lines.Select(l => new CreateSalesInvoiceLineRequest(
                l.Text, l.Qty, l.Price, world.SalesAccountId)).ToList());

    [Fact]
    public async Task Draft_HasNoNumberAndProducesNoPostings()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 1000m)));

        Assert.Equal("Draft", draft.State);
        Assert.Null(draft.DocNo);
        Assert.Null(draft.JournalEntryId);
        Assert.Equal(1000m, draft.Total);

        // A draft is not in the books at all.
        Assert.Equal(0, await db.Postings.CountAsync());
    }

    [Fact]
    public async Task Draft_DueDateComesFromTheCustomersCreditTerms()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 100m)));

        Assert.Equal(August.AddDays(30), draft.DueDate);
    }

    [Fact]
    public async Task Post_AssignsAGaplessNumberAndWritesAnEntry()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 4500m)));
        var posted = await invoices.PostAsync(draft.Id);

        Assert.Equal("Posted", posted.State);
        Assert.Equal("IV-2026-00001", posted.DocNo);
        Assert.NotNull(posted.JournalEntryId);
    }

    [Fact]
    public async Task Post_DebitsReceivablesWithTheCustomerAndCreditsRevenuePerLine()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, postings, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(
            Request(world, ("Advisory", 1m, 4500m), ("Valuation", 3m, 750m)));
        var posted = await invoices.PostAsync(draft.Id);

        var entry = await postings.GetAsync(posted.JournalEntryId!.Value);

        Assert.Equal(3, entry.Lines.Count);

        var receivable = entry.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);
        Assert.Equal("Debit", receivable.Direction);
        Assert.Equal(6750m, receivable.FunctionalAmount);

        // Not optional: receivables is a control account, and the database refuses a
        // posting to it without a customer.
        Assert.Equal(world.CustomerId, receivable.CustomerId);

        // Revenue is credited per line so a line's dimensions survive into the ledger.
        var revenue = entry.Lines.Where(l => l.AccountId == world.SalesAccountId).ToList();
        Assert.Equal(2, revenue.Count);
        Assert.All(revenue, l => Assert.Equal("Credit", l.Direction));
        Assert.Equal(6750m, revenue.Sum(l => l.FunctionalAmount));
    }

    [Fact]
    public async Task Post_Twice_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 100m)));
        await invoices.PostAsync(draft.Id);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(() => invoices.PostAsync(draft.Id));
        Assert.Contains("already posted", ex.Message);
    }

    [Fact]
    public async Task PostedInvoice_CannotBeChanged()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 100m)));
        var posted = await invoices.PostAsync(draft.Id);

        await using var editor = world.NewAppContext();
        var invoice = await editor.SalesInvoices.FirstAsync(i => i.Id == posted.Id);
        invoice.Memo = "tampered";

        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("posted and cannot be changed", ex!.GetBaseException().Message);
    }

    [Fact]
    public async Task DraftInvoice_CanStillBeChanged()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var draft = await invoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 100m)));

        await using var editor = world.NewAppContext();
        var invoice = await editor.SalesInvoices.FirstAsync(i => i.Id == draft.Id);
        invoice.Memo = "revised before posting";

        // A draft is ordinary mutable data â€” the freeze applies only once posted.
        await editor.SaveChangesAsync();

        Assert.Equal("revised before posting",
            (await editor.SalesInvoices.AsNoTracking().FirstAsync(i => i.Id == draft.Id)).Memo);
    }

    [Fact]
    public async Task Post_IntoAClosedPeriod_IsRejectedAndBurnsNoNumber()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var january = new CreateSalesInvoiceRequest(
            world.EntityId, world.CustomerId, new DateOnly(2026, 1, 15),
            [new CreateSalesInvoiceLineRequest("Retainer", 1m, 100m, world.SalesAccountId)]);

        var doomed = await invoices.CreateDraftAsync(january);

        await Assert.ThrowsAsync<PostingValidationException>(() => invoices.PostAsync(doomed.Id));

        // The rolled-back allocation must not leave a hole: the next invoice takes 00001.
        await using var second = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(second);
        var freshInvoices = new SalesInvoiceService(
            second, user, numbers,
            new PostingService(second, user, numbers, NullLogger<PostingService>.Instance),
            new SalesInvoicePostingRule(), NullLogger<SalesInvoiceService>.Instance);

        var good = await freshInvoices.CreateDraftAsync(Request(world, ("Retainer", 1m, 100m)));
        var posted = await freshInvoices.PostAsync(good.Id);

        Assert.Equal("IV-2026-00001", posted.DocNo);
    }

    [Fact]
    public async Task Draft_WithANonPositiveLine_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => invoices.CreateDraftAsync(Request(world, ("Freebie", 1m, 0m))));

        Assert.Contains("must be positive", ex.Message);
    }

    [Fact]
    public async Task Draft_ToAHeadingAccount_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var request = new CreateSalesInvoiceRequest(
            world.EntityId, world.CustomerId, August,
            [new CreateSalesInvoiceLineRequest("Retainer", 1m, 100m, world.HeadingAccountId)]);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => invoices.CreateDraftAsync(request));

        Assert.Contains("heading", ex.Message);
    }

    [Fact]
    public async Task Draft_ForeignCurrencyWithoutARate_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, _, db) = ServicesFor(world);
        await using var _1 = db;

        var request = new CreateSalesInvoiceRequest(
            world.EntityId, world.CustomerId, August,
            [new CreateSalesInvoiceLineRequest("Retainer", 1m, 100m, world.SalesAccountId)],
            CurrencyCode: "USD");

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => invoices.CreateDraftAsync(request));

        Assert.Contains("no exchange rate", ex.Message);
    }

    [Fact]
    public async Task PostedInvoices_ShowUpInTheTrialBalanceAndItStillBalances()
    {
        var world = await LedgerFixture.CreateAsync();
        var (invoices, postings, db) = ServicesFor(world);
        await using var _1 = db;

        var first = await invoices.CreateDraftAsync(Request(world, ("Advisory", 1m, 1000m)));
        await invoices.PostAsync(first.Id);
        var second = await invoices.CreateDraftAsync(Request(world, ("Valuation", 2m, 250m)));
        await invoices.PostAsync(second.Id);

        var trialBalance = await postings.GetTrialBalanceAsync(world.EntityId, new DateOnly(2026, 12, 31));

        Assert.True(trialBalance.IsBalanced);

        var receivables = trialBalance.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);
        Assert.Equal(1500m, receivables.Balance);
    }
}
