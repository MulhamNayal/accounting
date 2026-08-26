using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

/// <summary>
/// Credit notes on both sides.
/// </summary>
/// <remarks>
/// The load-bearing tests here are the two that check ageing still equals its control account
/// after a credit. That equality is why credit notes are required to name an invoice: a credit
/// on account would reduce the control account while leaving ageing untouched, and the two
/// would drift apart silently.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class CreditNoteTests
{
    private static readonly DateOnly InAugust2026 = new(2026, 8, 15);

    // ---------------------------------------------------------------- sales

    [Fact]
    public async Task PostAsync_CreditsReceivablesAndDebitsRevenueBack()
    {
        var w = await CreditWorld.CreateAsync();

        var invoice = await w.PostedInvoiceAsync(1000m);

        var note = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Goods returned damaged",
            [new CreateCreditNoteLine("Returned advisory", 1m, 400m, w.World.SalesAccountId)]));

        var posted = await w.SalesNotes.PostAsync(note.Id);

        Assert.Equal("Posted", posted.State);
        Assert.NotNull(posted.DocNo);
        Assert.Equal(400m, posted.TotalWithTax);

        await using var db = w.World.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        var receivable = postings.Single(p =>
            p.Account!.ControlType == ControlType.AccountsReceivable);
        Assert.Equal(PostingDirection.Credit, receivable.Direction);
        Assert.Equal(400m, receivable.Amount);
        Assert.Equal(w.World.CustomerId, receivable.CustomerId);

        var revenue = postings.Single(p => p.AccountId == w.World.SalesAccountId);
        Assert.Equal(PostingDirection.Debit, revenue.Direction);
        Assert.Equal(400m, revenue.Amount);
    }

    [Fact]
    public async Task PostAsync_ReducesWhatIsOutstandingOnTheInvoice()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(1000m);

        var note = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Partial return",
            [new CreateCreditNoteLine("Returned", 1m, 250m, w.World.SalesAccountId)]));
        await w.SalesNotes.PostAsync(note.Id);

        var open = await w.Receivables.GetOpenInvoicesAsync(w.EntityId, null);
        Assert.Equal(750m, open.Single(i => i.Id == invoice.Id).Outstanding);
    }

    [Fact]
    public async Task ADraftCreditNote_ReducesNothing()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(1000m);

        await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Thinking about it",
            [new CreateCreditNoteLine("Maybe", 1m, 900m, w.World.SalesAccountId)]));

        // A draft is not in the books, so it has not reduced anything.
        var open = await w.Receivables.GetOpenInvoicesAsync(w.EntityId, null);
        Assert.Equal(1000m, open.Single(i => i.Id == invoice.Id).Outstanding);
    }

    [Fact]
    public async Task CreateDraftAsync_ForMoreThanIsOutstanding_IsRefused()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(500m);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
                w.EntityId, invoice.Id, InAugust2026, "Too much",
                [new CreateCreditNoteLine("Over", 1m, 600m, w.World.SalesAccountId)])));

        Assert.Contains("credit on account", error.Message);
    }

    [Fact]
    public async Task PostAsync_SecondOfTwoDraftsThatTogetherExceedTheInvoice_IsRefused()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(500m);

        var first = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "First",
            [new CreateCreditNoteLine("A", 1m, 300m, w.World.SalesAccountId)]));
        var second = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Second",
            [new CreateCreditNoteLine("B", 1m, 300m, w.World.SalesAccountId)]));

        // Each is within the outstanding amount on its own; together they are not. Drafts do
        // not reserve anything, so the check has to bite at the moment of posting.
        await w.SalesNotes.PostAsync(first.Id);

        await Assert.ThrowsAsync<PostingValidationException>(() => w.SalesNotes.PostAsync(second.Id));
    }

    [Fact]
    public async Task CreateDraftAsync_WithoutAReason_IsRefused()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(100m);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
                w.EntityId, invoice.Id, InAugust2026, "   ",
                [new CreateCreditNoteLine("X", 1m, 50m, w.World.SalesAccountId)])));
    }

    [Fact]
    public async Task CreateDraftAsync_AgainstADraftInvoice_IsRefused()
    {
        var w = await CreditWorld.CreateAsync();

        var draft = await w.Invoices.CreateDraftAsync(new CreateSalesInvoiceRequest(
            w.EntityId, w.World.CustomerId, InAugust2026,
            [new CreateSalesInvoiceLineRequest("Not posted", 1m, 100m, w.World.SalesAccountId)]));

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
                w.EntityId, draft.Id, InAugust2026, "Nope",
                [new CreateCreditNoteLine("X", 1m, 50m, w.World.SalesAccountId)])));

        Assert.Contains("still a draft", error.Message);
    }

    [Fact]
    public async Task CreditNote_TakesTheInvoicesRateNotTodays()
    {
        var w = await CreditWorld.CreateAsync();

        // Invoiced 100 USD at 4.50.
        var invoice = await w.PostedInvoiceAsync(100m, currency: "USD", rate: 4.50m);

        var note = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Returned",
            [new CreateCreditNoteLine("Returned", 1m, 100m, w.World.SalesAccountId)]));

        // Not today's rate: crediting at a different one would leave a residue on the
        // receivable that no settlement ever clears.
        Assert.Equal("USD", note.CurrencyCode);
        Assert.Equal(4.50m, note.FxRate);

        var posted = await w.SalesNotes.PostAsync(note.Id);

        await using var db = w.World.NewAppContext();
        var receivable = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId
                        && p.Account!.ControlType == ControlType.AccountsReceivable)
            .SingleAsync();

        Assert.Equal(450m, receivable.FunctionalAmount);
    }

    [Fact]
    public async Task AgeingStillEqualsTheControlAccount_AfterACredit()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(1000m);

        var note = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Return",
            [new CreateCreditNoteLine("Returned", 1m, 300m, w.World.SalesAccountId)]));
        await w.SalesNotes.PostAsync(note.Id);

        var asOf = new DateOnly(2026, 8, 31);
        var ageing = await w.Receivables.GetAgeingAsync(w.EntityId, asOf);

        await using var db = w.World.NewAppContext();
        var control = await db.Postings
            .Where(p => p.LegalEntityId == w.EntityId
                        && p.Account!.ControlType == ControlType.AccountsReceivable
                        && p.JournalEntry!.EntryDate <= asOf)
            .SumAsync(p => p.Direction == PostingDirection.Debit
                ? p.FunctionalAmount
                : -p.FunctionalAmount);

        Assert.Equal(700m, ageing.Total);
        Assert.Equal(control, ageing.Total);
    }

    [Fact]
    public async Task APostedCreditNote_CannotBeAltered()
    {
        var w = await CreditWorld.CreateAsync();
        var invoice = await w.PostedInvoiceAsync(200m);

        var note = await w.SalesNotes.CreateDraftAsync(new CreateSalesCreditNoteRequest(
            w.EntityId, invoice.Id, InAugust2026, "Frozen",
            [new CreateCreditNoteLine("X", 1m, 100m, w.World.SalesAccountId)]));
        var posted = await w.SalesNotes.PostAsync(note.Id);

        await using var db = w.World.NewAppContext();
        var row = await db.SalesCreditNotes.FirstAsync(n => n.Id == posted.Id);
        row.ReasonCode = "tampered";

        // A posted credit note is the mechanism for undoing an invoice. If it were editable,
        // the invoice would be mutable again through the back door.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("posted", error.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- purchases

    [Fact]
    public async Task PurchaseCredit_DebitsPayablesAndCreditsTheChargeBack()
    {
        var w = await CreditWorld.CreateAsync();
        var bill = await w.PostedBillAsync("BILL-1", 800m);

        var note = await w.PurchaseNotes.CreateDraftAsync(new CreatePurchaseCreditNoteRequest(
            w.EntityId, bill.Id, InAugust2026, "Short delivery",
            [new CreateCreditNoteLine("Not delivered", 1m, 300m, w.ExpenseAccountId)]));

        var posted = await w.PurchaseNotes.PostAsync(note.Id);

        await using var db = w.World.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        var payable = postings.Single(p => p.Account!.ControlType == ControlType.AccountsPayable);
        Assert.Equal(PostingDirection.Debit, payable.Direction);
        Assert.Equal(300m, payable.Amount);
        Assert.Equal(w.SupplierId, payable.SupplierId);

        var charge = postings.Single(p => p.AccountId == w.ExpenseAccountId);
        Assert.Equal(PostingDirection.Credit, charge.Direction);
    }

    [Fact]
    public async Task PurchaseCredit_IrrecoverableTax_ComesBackOffTheCostNotInputTax()
    {
        var w = await CreditWorld.CreateAsync();
        var code = await w.AddTaxCodeAsync("SST-F", 8m, reclaimable: false);
        var bill = await w.PostedBillAsync("BILL-TAX", 1000m, taxCodeId: code);

        var note = await w.PurchaseNotes.CreateDraftAsync(new CreatePurchaseCreditNoteRequest(
            w.EntityId, bill.Id, InAugust2026, "Returned",
            [new CreateCreditNoteLine("Returned", 1m, 1000m, w.ExpenseAccountId, code)]));

        var posted = await w.PurchaseNotes.PostAsync(note.Id);

        await using var db = w.World.NewAppContext();
        var postings = await db.Postings
            .Include(p => p.Account)
            .Where(p => p.JournalEntryId == posted.JournalEntryId)
            .ToListAsync();

        // 1080 came off payables, 1080 came off the cost, and input tax was never involved --
        // crediting it would reclaim tax that was never claimed.
        Assert.Equal(1080m, postings.Single(p =>
            p.Account!.ControlType == ControlType.AccountsPayable).Amount);
        Assert.Equal(1080m, postings.Single(p => p.AccountId == w.ExpenseAccountId).Amount);
        Assert.DoesNotContain(postings, p => p.AccountId == w.InputTaxAccountId);
    }

    [Fact]
    public async Task PurchaseCredit_ReclaimableTax_ComesBackOffInputTax()
    {
        var w = await CreditWorld.CreateAsync();
        var code = await w.AddTaxCodeAsync("GST-R", 6m, reclaimable: true);
        var bill = await w.PostedBillAsync("BILL-R", 1000m, taxCodeId: code);

        var note = await w.PurchaseNotes.CreateDraftAsync(new CreatePurchaseCreditNoteRequest(
            w.EntityId, bill.Id, InAugust2026, "Returned",
            [new CreateCreditNoteLine("Returned", 1m, 1000m, w.ExpenseAccountId, code)]));

        var posted = await w.PurchaseNotes.PostAsync(note.Id);

        await using var db = w.World.NewAppContext();
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
    public async Task PayablesAgeingStillEqualsTheControlAccount_AfterACredit()
    {
        var w = await CreditWorld.CreateAsync();
        var bill = await w.PostedBillAsync("BILL-AGE", 900m);

        var note = await w.PurchaseNotes.CreateDraftAsync(new CreatePurchaseCreditNoteRequest(
            w.EntityId, bill.Id, InAugust2026, "Adjustment",
            [new CreateCreditNoteLine("Adjust", 1m, 200m, w.ExpenseAccountId)]));
        await w.PurchaseNotes.PostAsync(note.Id);

        var asOf = new DateOnly(2026, 8, 31);
        var ageing = await w.Payables.GetAgeingAsync(w.EntityId, asOf);

        await using var db = w.World.NewAppContext();
        var control = await db.Postings
            .Where(p => p.LegalEntityId == w.EntityId
                        && p.Account!.ControlType == ControlType.AccountsPayable
                        && p.JournalEntry!.EntryDate <= asOf)
            .SumAsync(p => p.Direction == PostingDirection.Credit
                ? p.FunctionalAmount
                : -p.FunctionalAmount);

        Assert.Equal(700m, ageing.TotalOutstanding);
        Assert.Equal(control, ageing.TotalOutstanding);
    }

    [Fact]
    public async Task PurchaseCredit_ForMoreThanIsOwed_IsRefused()
    {
        var w = await CreditWorld.CreateAsync();
        var bill = await w.PostedBillAsync("BILL-OVER", 100m);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            w.PurchaseNotes.CreateDraftAsync(new CreatePurchaseCreditNoteRequest(
                w.EntityId, bill.Id, InAugust2026, "Too much",
                [new CreateCreditNoteLine("Over", 1m, 200m, w.ExpenseAccountId)])));
    }

    // ---------------------------------------------------------------- fixture

    private sealed class CreditWorld
    {
        public LedgerWorld World { get; private set; } = null!;
        public Guid EntityId => World.EntityId;

        public Guid SupplierId { get; private set; }
        public Guid ExpenseAccountId { get; private set; }
        public Guid InputTaxAccountId { get; private set; }
        public Guid OutputTaxAccountId { get; private set; }
        public Guid ReclaimableRegimeId { get; private set; }
        public Guid FinalRegimeId { get; private set; }

        public ISalesInvoiceService Invoices { get; private set; } = null!;
        public IReceivablesService Receivables { get; private set; } = null!;
        public ISalesCreditNoteService SalesNotes { get; private set; } = null!;
        public IPurchaseInvoiceService Bills { get; private set; } = null!;
        public IPayablesService Payables { get; private set; } = null!;
        public IPurchaseCreditNoteService PurchaseNotes { get; private set; } = null!;

        public static async Task<CreditWorld> CreateAsync()
        {
            var world = await LedgerFixture.CreateAsync();
            var w = new CreditWorld { World = world };

            await using var setup = world.NewAppContext();

            var payables = NewAccount(world.TenantId, "2010", "Trade Payables",
                AccountType.Liability, ControlType.AccountsPayable);
            var inputTax = NewAccount(world.TenantId, "1240", "Input Tax",
                AccountType.Asset, ControlType.Tax);
            var outputTax = NewAccount(world.TenantId, "2020", "Output Tax",
                AccountType.Liability, ControlType.Tax);
            var expense = NewAccount(world.TenantId, "6200", "Office Costs", AccountType.Expense);
            setup.Accounts.AddRange(payables, inputTax, outputTax, expense);

            var reclaimable = NewRegime(world.TenantId, "R-GST", reclaimable: true);
            var final = NewRegime(world.TenantId, "F-SST", reclaimable: false);
            setup.TaxRegimes.AddRange(reclaimable, final);

            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                Code = "S0001",
                Name = "Test Supplier",
                CurrencyCode = "MYR",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            setup.Suppliers.Add(supplier);

            foreach (var (type, code, gapless) in new[]
                     {
                         // The shared fixture covers journals, invoices and receipts only.
                         ("CreditNote", "CN", true),
                         ("PurchaseInvoice", "PI", false),
                         ("SupplierPayment", "PV", false),
                         ("PurchaseCreditNote", "SC", true),
                     })
            {
                setup.NumberSeries.Add(new NumberSeries
                {
                    Id = Guid.NewGuid(),
                    TenantId = world.TenantId,
                    LegalEntityId = world.EntityId,
                    DocumentType = type,
                    Code = code,
                    Name = code,
                    Format = code + "-{0:D5}",
                    ResetPolicy = NumberResetPolicy.Yearly,
                    IsGapless = gapless,
                    IsDefault = true,
                });
            }

            await setup.SaveChangesAsync();

            w.ExpenseAccountId = expense.Id;
            w.InputTaxAccountId = inputTax.Id;
            w.OutputTaxAccountId = outputTax.Id;
            w.ReclaimableRegimeId = reclaimable.Id;
            w.FinalRegimeId = final.Id;
            w.SupplierId = supplier.Id;

            // One shared context, as scoped DI gives the running application per request. A
            // context per service would put a number allocation in a different transaction
            // from the document it numbers.
            var db = world.NewAppContext();
            var user = new CurrentUser();
            user.SetUser(world.UserId);
            var numbers = new NumberSeriesService(db);
            var postings = new PostingService(db, user, numbers, NullLogger<PostingService>.Instance);

            w.Invoices = new SalesInvoiceService(
                db, user, numbers, postings, new SalesInvoicePostingRule(),
                NullLogger<SalesInvoiceService>.Instance);
            w.Receivables = new ReceivablesService(
                db, user, numbers, postings, NullLogger<ReceivablesService>.Instance);
            w.SalesNotes = new SalesCreditNoteService(
                db, user, numbers, postings, w.Receivables, new SalesCreditNotePostingRule(),
                NullLogger<SalesCreditNoteService>.Instance);
            w.Bills = new PurchaseInvoiceService(
                db, user, numbers, postings, new PurchaseInvoicePostingRule(),
                NullLogger<PurchaseInvoiceService>.Instance);
            w.Payables = new PayablesService(
                db, user, numbers, postings, NullLogger<PayablesService>.Instance);
            w.PurchaseNotes = new PurchaseCreditNoteService(
                db, user, numbers, postings, w.Payables, new PurchaseCreditNotePostingRule(),
                NullLogger<PurchaseCreditNoteService>.Instance);

            return w;
        }

        public async Task<SalesInvoiceDetail> PostedInvoiceAsync(
            decimal amount, string? currency = null, decimal? rate = null)
        {
            var draft = await Invoices.CreateDraftAsync(new CreateSalesInvoiceRequest(
                World.EntityId, World.CustomerId, InAugust2026,
                [new CreateSalesInvoiceLineRequest("Advisory", 1m, amount, World.SalesAccountId)],
                CurrencyCode: currency,
                FxRate: rate));

            return await Invoices.PostAsync(draft.Id);
        }

        public async Task<PurchaseInvoiceDetail> PostedBillAsync(
            string reference, decimal amount, Guid? taxCodeId = null)
        {
            var draft = await Bills.CreateDraftAsync(new CreatePurchaseInvoiceRequest(
                World.EntityId, SupplierId, reference, InAugust2026,
                [new CreatePurchaseInvoiceLine("Goods", 1m, amount, ExpenseAccountId, taxCodeId)]));

            return await Bills.PostAsync(draft.Id);
        }

        public async Task<Guid> AddTaxCodeAsync(string code, decimal rate, bool reclaimable)
        {
            await using var db = World.NewAppContext();
            var taxCode = new TaxCode
            {
                Id = Guid.NewGuid(),
                TenantId = World.TenantId,
                TaxRegimeId = reclaimable ? ReclaimableRegimeId : FinalRegimeId,
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

        private static TaxRegime NewRegime(Guid tenantId, string code, bool reclaimable) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = code,
            CountryCode = "MY",
            InputReclaimable = reclaimable,
            EffectiveFrom = new DateOnly(2015, 1, 1),
        };

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
