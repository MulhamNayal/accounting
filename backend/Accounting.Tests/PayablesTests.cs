using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

/// <summary>
/// Purchase invoices, payments, allocation and the payables subledger.
/// </summary>
/// <remarks>
/// Most of this mirrors <see cref="ReceivablesTests"/>, deliberately. The tests worth reading
/// are the two things payables does that sales does not: refusing a bill already entered, and
/// putting irrecoverable tax into the cost rather than treating it as an asset.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class PayablesTests
{
    private static readonly DateOnly InAugust2026 = new(2026, 8, 15);

    // ---------------------------------------------------------------- bills

    [Fact]
    public async Task PostAsync_CreditsPayablesGrossAndDebitsTheCharge()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "INV-001", InAugust2026,
            [new CreatePurchaseInvoiceLine("Office chairs", 2m, 300m, w.ExpenseAccountId)]));

        var posted = await w.Invoices.PostAsync(invoice.Id);

        Assert.Equal("Posted", posted.State);
        Assert.NotNull(posted.DocNo);
        Assert.Equal(600m, posted.TotalWithTax);

        await using var db = world.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        var payable = postings.Single(p => p.Account!.ControlType == ControlType.AccountsPayable);
        Assert.Equal(PostingDirection.Credit, payable.Direction);
        Assert.Equal(600m, payable.Amount);
        Assert.Equal(w.SupplierId, payable.SupplierId);

        var charge = postings.Single(p => p.AccountId == w.ExpenseAccountId);
        Assert.Equal(PostingDirection.Debit, charge.Direction);
        Assert.Equal(600m, charge.Amount);
    }

    [Fact]
    public async Task CreateDraftAsync_SameSupplierInvoiceNumberTwice_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "DUP-7", InAugust2026,
            [new CreatePurchaseInvoiceLine("Consulting", 1m, 900m, w.ExpenseAccountId)]));

        var again = await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
                world.EntityId, w.SupplierId, "DUP-7", InAugust2026.AddDays(3),
                [new CreatePurchaseInvoiceLine("Consulting", 1m, 900m, w.ExpenseAccountId)])));

        Assert.Contains("already recorded", again.Message);
    }

    [Fact]
    public async Task CreateDraftAsync_SameNumberFromADifferentSupplier_IsAllowed()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);
        var other = await w.AddSupplierAsync("S0002", "Another Supplier", "MYR");

        await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "0001", InAugust2026,
            [new CreatePurchaseInvoiceLine("Rent", 1m, 500m, w.ExpenseAccountId)]));

        // Suppliers number their own invoices; a collision across two of them means nothing.
        var second = await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, other, "0001", InAugust2026,
            [new CreatePurchaseInvoiceLine("Rent", 1m, 500m, w.ExpenseAccountId)]));

        Assert.Equal("0001", second.SupplierInvoiceNo);
    }

    [Fact]
    public async Task PostAsync_ReclaimableTax_GoesToInputTaxNotTheCharge()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);
        var code = await w.AddTaxCodeAsync("GST-SR", 6m, reclaimable: true);

        var draft = await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "TAX-1", InAugust2026,
            [new CreatePurchaseInvoiceLine("Supplies", 1m, 1000m, w.ExpenseAccountId, code)]));

        Assert.True(draft.Lines[0].TaxReclaimable);
        Assert.Equal(60m, draft.Lines[0].TaxAmount);
        // The charge bears the net only; the tax is recoverable and therefore an asset.
        Assert.Equal(1000m, draft.Lines[0].ChargeAmount);

        var posted = await w.Invoices.PostAsync(draft.Id);

        await using var db = world.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        Assert.Equal(1060m, postings.Single(p =>
            p.Account!.ControlType == ControlType.AccountsPayable).Amount);
        Assert.Equal(1000m, postings.Single(p => p.AccountId == w.ExpenseAccountId).Amount);
        Assert.Equal(60m, postings.Single(p => p.AccountId == w.InputTaxAccountId).Amount);
    }

    [Fact]
    public async Task PostAsync_IrrecoverableTax_IsAddedToTheChargeInstead()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);
        var code = await w.AddTaxCodeAsync("SST-SV", 8m, reclaimable: false);

        var draft = await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "TAX-2", InAugust2026,
            [new CreatePurchaseInvoiceLine("Services", 1m, 1000m, w.ExpenseAccountId, code)]));

        Assert.False(draft.Lines[0].TaxReclaimable);
        Assert.Equal(80m, draft.Lines[0].TaxAmount);
        // Tax that cannot be reclaimed is part of what the thing cost.
        Assert.Equal(1080m, draft.Lines[0].ChargeAmount);

        var posted = await w.Invoices.PostAsync(draft.Id);

        await using var db = world.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        Assert.Equal(1080m, postings.Single(p =>
            p.Account!.ControlType == ControlType.AccountsPayable).Amount);
        Assert.Equal(1080m, postings.Single(p => p.AccountId == w.ExpenseAccountId).Amount);

        // Nothing reaches input tax. Treating irrecoverable tax as an asset would overstate
        // the balance sheet and understate costs, invisibly.
        Assert.DoesNotContain(postings, p => p.AccountId == w.InputTaxAccountId);
    }

    [Fact]
    public async Task PostAsync_Twice_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var draft = await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "ONCE-1", InAugust2026,
            [new CreatePurchaseInvoiceLine("Thing", 1m, 100m, w.ExpenseAccountId)]));

        await w.Invoices.PostAsync(draft.Id);

        await Assert.ThrowsAsync<PostingValidationException>(() => w.Invoices.PostAsync(draft.Id));
    }

    [Fact]
    public async Task CreateDraftAsync_ChargedToAControlAccount_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
                world.EntityId, w.SupplierId, "BAD-1", InAugust2026,
                [new CreatePurchaseInvoiceLine("Nope", 1m, 100m, world.ReceivablesAccountId)])));
    }

    // ---------------------------------------------------------------- payments

    [Fact]
    public async Task PostPaymentAsync_DebitsPayablesAndCreditsTheBank()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var payment = await w.Payables.CreatePaymentAsync(new CreatePaymentRequest(
            world.EntityId, w.SupplierId, world.CashAccountId, InAugust2026, 400m));

        var posted = await w.Payables.PostPaymentAsync(payment.Id);

        Assert.Equal("Posted", posted.State);
        Assert.Equal(400m, posted.Unallocated);

        await using var db = world.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        var payable = postings.Single(p => p.Account!.ControlType == ControlType.AccountsPayable);
        Assert.Equal(PostingDirection.Debit, payable.Direction);
        Assert.Equal(w.SupplierId, payable.SupplierId);

        var bank = postings.Single(p => p.AccountId == world.CashAccountId);
        Assert.Equal(PostingDirection.Credit, bank.Direction);
    }

    [Fact]
    public async Task CreatePaymentAsync_FromANonBankAccount_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Payables.CreatePaymentAsync(new CreatePaymentRequest(
                world.EntityId, w.SupplierId, w.ExpenseAccountId, InAugust2026, 100m)));
    }

    // ---------------------------------------------------------------- allocation

    [Fact]
    public async Task AllocateAsync_SettlesTheBillAndClearsItFromAgeing()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("ALLOC-1", 500m);
        var payment = await w.PostedPaymentAsync(500m);

        var allocations = await w.Payables.AllocateAsync(
            new AllocatePaymentRequest(payment.Id, [new AllocatePaymentLine(invoice.Id, 500m)]));

        Assert.Single(allocations);
        Assert.Equal(500m, allocations[0].Amount);
        Assert.Equal(0m, allocations[0].FxGainLossFunctional);

        var open = await w.Payables.GetOpenInvoicesAsync(world.EntityId, null);
        Assert.DoesNotContain(open, i => i.Id == invoice.Id);

        var ageing = await w.Payables.GetAgeingAsync(world.EntityId, new DateOnly(2026, 8, 31));
        Assert.Equal(0m, ageing.TotalOutstanding);
    }

    [Fact]
    public async Task AllocateAsync_MoreThanWasPaid_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("OVER-1", 900m);
        var payment = await w.PostedPaymentAsync(100m);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Payables.AllocateAsync(new AllocatePaymentRequest(
                payment.Id, [new AllocatePaymentLine(invoice.Id, 900m)])));

        Assert.Contains("invent money", error.Message);
    }

    [Fact]
    public async Task AllocateAsync_AgainstAnotherSuppliersBill_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);
        var other = await w.AddSupplierAsync("S0009", "Unrelated Supplier", "MYR");

        var invoice = await w.PostedInvoiceAsync("X-1", 200m, other);
        var payment = await w.PostedPaymentAsync(200m);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Payables.AllocateAsync(new AllocatePaymentRequest(
                payment.Id, [new AllocatePaymentLine(invoice.Id, 200m)])));
    }

    [Fact]
    public async Task UnallocateAsync_InsertsAReversalRatherThanDeleting()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("UN-1", 300m);
        var payment = await w.PostedPaymentAsync(300m);

        var allocated = await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(invoice.Id, 300m)]));

        var reversal = await w.Payables.UnallocateAsync(allocated[0].Id);

        Assert.Equal(-300m, reversal.Amount);
        Assert.Equal(allocated[0].Id, reversal.ReversesAllocationId);

        await using var db = world.NewAppContext();
        // Both rows survive. The original is never removed, because which bill a payment
        // cleared is a fact worth keeping.
        Assert.Equal(2, await db.PaymentAllocations
            .CountAsync(a => a.SupplierPaymentId == payment.Id));

        var open = await w.Payables.GetOpenInvoicesAsync(world.EntityId, null);
        Assert.Contains(open, i => i.Id == invoice.Id && i.Outstanding == 300m);
    }

    [Fact]
    public async Task UnallocateAsync_Twice_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("UN-2", 150m);
        var payment = await w.PostedPaymentAsync(150m);

        var allocated = await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(invoice.Id, 150m)]));

        await w.Payables.UnallocateAsync(allocated[0].Id);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Payables.UnallocateAsync(allocated[0].Id));
    }

    // ---------------------------------------------------------------- exchange differences

    [Fact]
    public async Task AllocateAsync_PayingLessThanWasOwed_RealisesAGain()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        // Billed 100 USD at 4.50, paid at 4.20: 30 MYR less went out than was owed.
        var invoice = await w.PostedInvoiceAsync("FX-1", 100m, currency: "USD", fxRate: 4.50m);
        var payment = await w.PostedPaymentAsync(100m, currency: "USD", fxRate: 4.20m);

        var allocations = await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(invoice.Id, 100m)]));

        // Positive is a gain on the payables side, the opposite sign to receivables.
        Assert.Equal(30m, allocations[0].FxGainLossFunctional);
        Assert.NotNull(allocations[0].JournalEntryId);

        await using var db = world.NewAppContext();
        var fx = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == allocations[0].JournalEntryId)
            .ToListAsync();

        // Payables is debited to clear the residue; the gain is credited.
        Assert.Equal(PostingDirection.Debit,
            fx.Single(p => p.Account!.ControlType == ControlType.AccountsPayable).Direction);
        Assert.Equal(PostingDirection.Credit,
            fx.Single(p => p.AccountId == world.FxAccountId).Direction);

        // And the supplier's balance is now nil in the functional currency too.
        var statement = await w.Payables.GetStatementAsync(
            world.EntityId, w.SupplierId, new DateOnly(2026, 8, 31));
        Assert.Equal(0m, statement.ClosingBalance);
    }

    [Fact]
    public async Task AllocateAsync_AcrossCurrencies_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("FX-2", 100m, currency: "USD", fxRate: 4.5m);
        var payment = await w.PostedPaymentAsync(450m);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.Payables.AllocateAsync(new AllocatePaymentRequest(
                payment.Id, [new AllocatePaymentLine(invoice.Id, 100m)])));
    }

    // ---------------------------------------------------------------- reporting

    [Fact]
    public async Task GetAgeingAsync_TotalEqualsThePayablesControlAccount()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await w.PostedInvoiceAsync("AGE-1", 400m);
        await w.PostedInvoiceAsync("AGE-2", 250m);
        var payment = await w.PostedPaymentAsync(100m);
        var second = (await w.Payables.GetOpenInvoicesAsync(world.EntityId, null))
            .First(i => i.SupplierInvoiceNo == "AGE-2");
        await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(second.Id, 100m)]));

        var asOf = new DateOnly(2026, 8, 31);
        var ageing = await w.Payables.GetAgeingAsync(world.EntityId, asOf);

        await using var db = world.NewAppContext();
        var controlBalance = await db.Postings
            .Where(p => p.LegalEntityId == world.EntityId
                        && p.Account!.ControlType == ControlType.AccountsPayable
                        && p.JournalEntry!.EntryDate <= asOf)
            .SumAsync(p => p.Direction == PostingDirection.Credit
                ? p.FunctionalAmount
                : -p.FunctionalAmount);

        // The subledger and the control account are the same postings summed differently, so
        // this is not a reconciliation -- a difference would mean one of them is wrong.
        Assert.Equal(550m, ageing.TotalOutstanding);
        Assert.Equal(controlBalance, ageing.TotalOutstanding);
    }

    [Fact]
    public async Task GetStatementAsync_ReadsPositiveWhenMoneyIsOwed()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await w.PostedInvoiceAsync("ST-1", 700m);
        var payment = await w.PostedPaymentAsync(200m);
        var invoice = (await w.Payables.GetOpenInvoicesAsync(world.EntityId, null)).Single();
        await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(invoice.Id, 200m)]));

        var statement = await w.Payables.GetStatementAsync(
            world.EntityId, w.SupplierId, new DateOnly(2026, 8, 31));

        Assert.Equal(2, statement.Lines.Count);
        Assert.Equal(500m, statement.ClosingBalance);
    }

    [Fact]
    public async Task GetOpenInvoicesAsync_ExcludesDrafts()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        await w.Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
            world.EntityId, w.SupplierId, "DRAFT-1", InAugust2026,
            [new CreatePurchaseInvoiceLine("Not yet", 1m, 999m, w.ExpenseAccountId)]));

        var open = await w.Payables.GetOpenInvoicesAsync(world.EntityId, null);

        // A draft is not owed. It is not in the books at all.
        Assert.Empty(open);
    }

    // ---------------------------------------------------------------- immutability

    [Fact]
    public async Task APostedBill_CannotBeAltered()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("FROZEN-1", 100m);

        await using var db = world.NewAppContext();
        var row = await db.PurchaseInvoices.FirstAsync(i => i.Id == invoice.Id);
        row.Memo = "tampered";

        // Enforced by a trigger, not by the service: a posted document is frozen whatever
        // opens the connection.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("posted", error.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APaymentAllocation_CannotBeUpdatedOrDeleted()
    {
        var world = await LedgerFixture.CreateAsync();
        var w = await PayablesWorld.CreateAsync(world);

        var invoice = await w.PostedInvoiceAsync("APPEND-1", 100m);
        var payment = await w.PostedPaymentAsync(100m);
        var allocated = await w.Payables.AllocateAsync(new AllocatePaymentRequest(
            payment.Id, [new AllocatePaymentLine(invoice.Id, 100m)]));

        await using var db = world.NewAppContext();
        var row = await db.PaymentAllocations.FirstAsync(a => a.Id == allocated[0].Id);
        row.Amount = 1m;

        // UPDATE and DELETE are revoked from the application role outright.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("permission denied", error.InnerException!.Message);
    }

    // ---------------------------------------------------------------- fixture

    /// <summary>
    /// The payables-specific arrangement: a supplier, a payables control account, an input tax
    /// account and an expense to charge things to. Built here rather than in
    /// <see cref="LedgerFixture"/> so the existing suite is untouched.
    /// </summary>
    private sealed class PayablesWorld
    {
        private readonly LedgerWorld _world;

        private PayablesWorld(LedgerWorld world) => _world = world;

        public Guid SupplierId { get; private set; }
        public Guid PayablesAccountId { get; private set; }
        public Guid InputTaxAccountId { get; private set; }
        public Guid OutputTaxAccountId { get; private set; }
        public Guid ExpenseAccountId { get; private set; }
        public Guid TaxRegimeReclaimableId { get; private set; }
        public Guid TaxRegimeFinalId { get; private set; }

        // Built once over ONE DbContext, exactly as scoped DI gives the real application a
        // single context per request. A fresh context per service would put the number
        // allocation in a different transaction from the entry it numbers, and two documents
        // would take the same number.
        public IPurchaseInvoiceService Invoices { get; private set; } = null!;

        public IPayablesService Payables { get; private set; } = null!;

        public static async Task<PayablesWorld> CreateAsync(LedgerWorld world)
        {
            var w = new PayablesWorld(world);

            await using var db = world.NewAppContext();

            var payables = NewAccount(world.TenantId, "2010", "Trade Payables",
                AccountType.Liability, ControlType.AccountsPayable);
            var inputTax = NewAccount(world.TenantId, "1240", "Input Tax",
                AccountType.Asset, ControlType.Tax);
            // A tax code is one record used by both sides, and ck_tax_code_has_output_account
            // requires any code with a rate to say where output tax is credited -- even a code
            // these tests only ever purchase with.
            var outputTax = NewAccount(world.TenantId, "2020", "Output Tax",
                AccountType.Liability, ControlType.Tax);
            var expense = NewAccount(world.TenantId, "6200", "Office Costs", AccountType.Expense);
            db.Accounts.AddRange(payables, inputTax, outputTax, expense);

            var reclaimable = new TaxRegime
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                Code = "R-GST",
                Name = "Reclaimable regime",
                CountryCode = "MY",
                InputReclaimable = true,
                EffectiveFrom = new DateOnly(2015, 1, 1),
            };
            var final = new TaxRegime
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                Code = "F-SST",
                Name = "Non-reclaimable regime",
                CountryCode = "MY",
                InputReclaimable = false,
                EffectiveFrom = new DateOnly(2015, 1, 1),
            };
            db.TaxRegimes.AddRange(reclaimable, final);

            db.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                Code = "S0001",
                Name = "Test Supplier",
                CurrencyCode = "MYR",
                CreditTermDays = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            // The fixture's series cover journals, invoices and receipts only.
            foreach (var (type, code) in new[]
                     {
                         ("PurchaseInvoice", "PI"),
                         ("SupplierPayment", "PV"),
                     })
            {
                db.NumberSeries.Add(new NumberSeries
                {
                    Id = Guid.NewGuid(),
                    TenantId = world.TenantId,
                    LegalEntityId = world.EntityId,
                    DocumentType = type,
                    Code = code,
                    Name = code,
                    Format = code + "-{0:D5}",
                    ResetPolicy = NumberResetPolicy.Yearly,
                    IsGapless = false,
                    IsDefault = true,
                });
            }

            await db.SaveChangesAsync();

            w.PayablesAccountId = payables.Id;
            w.InputTaxAccountId = inputTax.Id;
            w.OutputTaxAccountId = outputTax.Id;
            w.ExpenseAccountId = expense.Id;
            w.TaxRegimeReclaimableId = reclaimable.Id;
            w.TaxRegimeFinalId = final.Id;
            w.SupplierId = await db.Suppliers.Where(s => s.Code == "S0001")
                .Select(s => s.Id).FirstAsync();

            var shared = world.NewAppContext();
            var user = new CurrentUser();
            user.SetUser(world.UserId);
            var numbers = new NumberSeriesService(shared);
            var postings = new PostingService(
                shared, user, numbers, NullLogger<PostingService>.Instance);

            w.Invoices = new PurchaseInvoiceService(
                shared, user, numbers, postings, new PurchaseInvoicePostingRule(),
                NullLogger<PurchaseInvoiceService>.Instance);
            w.Payables = new PayablesService(
                shared, user, numbers, postings, NullLogger<PayablesService>.Instance);

            return w;
        }

        public async Task<Guid> AddSupplierAsync(string code, string name, string currency)
        {
            await using var db = _world.NewAppContext();
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = _world.TenantId,
                Code = code,
                Name = name,
                CurrencyCode = currency,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            return supplier.Id;
        }

        public async Task<Guid> AddTaxCodeAsync(string code, decimal rate, bool reclaimable)
        {
            await using var db = _world.NewAppContext();
            var taxCode = new TaxCode
            {
                Id = Guid.NewGuid(),
                TenantId = _world.TenantId,
                TaxRegimeId = reclaimable ? TaxRegimeReclaimableId : TaxRegimeFinalId,
                Code = code,
                Name = code,
                Kind = TaxKind.ValueAdded,
                Rate = rate,
                InputAccountId = reclaimable ? InputTaxAccountId : null,
                OutputAccountId = OutputTaxAccountId,
                EffectiveFrom = new DateOnly(2015, 1, 1),
            };
            db.TaxCodes.Add(taxCode);
            await db.SaveChangesAsync();
            return taxCode.Id;
        }

        public async Task<PurchaseInvoiceDetail> PostedInvoiceAsync(
            string supplierInvoiceNo,
            decimal amount,
            Guid? supplierId = null,
            string? currency = null,
            decimal? fxRate = null)
        {
            var draft = await Invoices.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
                _world.EntityId,
                supplierId ?? SupplierId,
                supplierInvoiceNo,
                InAugust2026,
                [new CreatePurchaseInvoiceLine("Goods", 1m, amount, ExpenseAccountId)],
                CurrencyCode: currency,
                FxRate: fxRate));

            return await Invoices.PostAsync(draft.Id);
        }

        public async Task<PaymentSummary> PostedPaymentAsync(
            decimal amount, string? currency = null, decimal? fxRate = null)
        {
            var payment = await Payables.CreatePaymentAsync(new CreatePaymentRequest(
                _world.EntityId, SupplierId, _world.CashAccountId, InAugust2026, amount,
                CurrencyCode: currency, FxRate: fxRate));

            return await Payables.PostPaymentAsync(payment.Id);
        }

        private static Account NewAccount(
            Guid tenantId, string code, string name, AccountType type,
            ControlType control = ControlType.None) => new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                AccountType = type,
                ControlType = control,
            };
    }
}
