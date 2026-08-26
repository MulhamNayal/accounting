using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

[Collection(nameof(DatabaseCollection))]
public class TaxTests
{
    private static readonly DateOnly August = new(2026, 8, 15);

    private sealed record Kit(
        ISalesInvoiceService Invoices,
        IPostingService Postings,
        ITaxService Tax,
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
        return new Kit(invoices, postings, new TaxService(db), db);
    }

    /// <summary>
    /// A current regime with a rated code, plus a superseded one, so effective dating has
    /// something to distinguish.
    /// </summary>
    private static async Task<(Guid CurrentCodeId, Guid RetiredCodeId, Guid ZeroCodeId)>
        SeedRegimesAsync(LedgerWorld world)
    {
        await using var db = world.NewAppContext();

        var outputTax = new Account
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = "2020",
            Name = "Output Tax",
            AccountType = AccountType.Liability,
            ControlType = ControlType.Tax,
        };
        db.Accounts.Add(outputTax);

        var retired = new TaxRegime
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = "MY-GST",
            Name = "Historical GST",
            CountryCode = "MY",
            InputReclaimable = true,
            EffectiveFrom = new DateOnly(2015, 4, 1),
            EffectiveTo = new DateOnly(2018, 8, 31),
        };

        var current = new TaxRegime
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = "MY-SST",
            Name = "Sales and Service Tax",
            CountryCode = "MY",
            InputReclaimable = false,
            EffectiveFrom = new DateOnly(2018, 9, 1),
        };

        db.TaxRegimes.AddRange(retired, current);

        var currentCode = new TaxCode
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            TaxRegimeId = current.Id,
            Code = "SV",
            Name = "Service tax",
            Kind = TaxKind.ServiceTax,
            Rate = 8m,
            OutputAccountId = outputTax.Id,
            EffectiveFrom = current.EffectiveFrom,
        };

        var retiredCode = new TaxCode
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            TaxRegimeId = retired.Id,
            Code = "SR",
            Name = "Standard rated",
            Kind = TaxKind.ValueAdded,
            Rate = 6m,
            OutputAccountId = outputTax.Id,
            EffectiveFrom = retired.EffectiveFrom,
            EffectiveTo = retired.EffectiveTo,
        };

        var zeroCode = new TaxCode
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            TaxRegimeId = current.Id,
            Code = "NA",
            Name = "Not taxable",
            Kind = TaxKind.OutOfScope,
            Rate = 0m,
            EffectiveFrom = current.EffectiveFrom,
        };

        db.TaxCodes.AddRange(currentCode, retiredCode, zeroCode);
        await db.SaveChangesAsync();

        return (currentCode.Id, retiredCode.Id, zeroCode.Id);
    }

    private static CreateSalesInvoiceRequest Invoice(
        LedgerWorld world, decimal price, Guid? taxCodeId, DateOnly? date = null) => new(
            world.EntityId, world.CustomerId, date ?? August,
            [new CreateSalesInvoiceLineRequest(
                "Advisory", 1m, price, world.SalesAccountId, TaxCodeId: taxCodeId)]);

    [Fact]
    public async Task TaxedInvoice_AddsTaxToWhatTheCustomerOwes()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 1000m, currentCode));

        Assert.Equal(1000m, draft.Total);
        Assert.Equal(80m, draft.TaxTotal);      // 8% service tax
        Assert.Equal(1080m, draft.TotalWithTax);
        Assert.Equal(8m, draft.Lines[0].TaxRate);
    }

    [Fact]
    public async Task TaxedInvoice_PostsReceivablesGrossAndOutputTaxSeparately()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 1000m, currentCode));
        var posted = await kit.Invoices.PostAsync(draft.Id);
        var entry = await kit.Postings.GetAsync(posted.JournalEntryId!.Value);

        // Receivables carries what is owed: net plus tax.
        var receivable = entry.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);
        Assert.Equal(1080m, receivable.FunctionalAmount);

        // Revenue is credited net â€” tax was never income.
        var revenue = entry.Lines.Single(l => l.AccountId == world.SalesAccountId);
        Assert.Equal(1000m, revenue.FunctionalAmount);

        // Output tax stands on its own line, tagged with the code that produced it, so a
        // return can be filed from the ledger rather than from the documents.
        var tax = entry.Lines.Single(l =>
            l.AccountId != world.ReceivablesAccountId && l.AccountId != world.SalesAccountId);
        Assert.Equal(80m, tax.FunctionalAmount);
        Assert.Equal("Credit", tax.Direction);
    }

    [Fact]
    public async Task ZeroRatedCode_ChargesNothingButIsStillRecorded()
    {
        var world = await LedgerFixture.CreateAsync();
        var (_, _, zeroCode) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 500m, zeroCode));
        var posted = await kit.Invoices.PostAsync(draft.Id);
        var entry = await kit.Postings.GetAsync(posted.JournalEntryId!.Value);

        Assert.Equal(0m, posted.TaxTotal);
        Assert.Equal(500m, posted.TotalWithTax);

        // Two lines, not three: nothing to post, but the code is on the revenue line so a
        // return can still distinguish it from a line outside the regime altogether.
        Assert.Equal(2, entry.Lines.Count);
        Assert.NotNull(draft.Lines[0].TaxCodeId);
    }

    [Fact]
    public async Task UntaxedLine_IsDistinctFromZeroRated()
    {
        var world = await LedgerFixture.CreateAsync();
        await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 500m, taxCodeId: null));

        Assert.Null(draft.Lines[0].TaxCodeId);
        Assert.Equal(0m, draft.Lines[0].TaxRate);
    }

    [Fact]
    public async Task ARetiredCode_CannotBeUsedOnATodayDocument()
    {
        var world = await LedgerFixture.CreateAsync();
        var (_, retiredCode, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Invoices.CreateDraftAsync(Invoice(world, 100m, retiredCode)));

        Assert.Contains("does not apply on", ex.Message);
    }

    [Fact]
    public async Task ACurrentCode_CannotBeUsedOnABackdatedDocument()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        // 2017 predates the current regime. Getting this wrong is how a GST-era invoice ends
        // up restated under SST.
        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Invoices.CreateDraftAsync(
                Invoice(world, 100m, currentCode, new DateOnly(2017, 6, 1))));

        Assert.Contains("does not apply on", ex.Message);
    }

    [Fact]
    public async Task AvailableCodes_DependOnTheDocumentDate()
    {
        var world = await LedgerFixture.CreateAsync();
        await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var today = await kit.Tax.ListCodesAsync(August);
        var gstEra = await kit.Tax.ListCodesAsync(new DateOnly(2017, 6, 1));

        Assert.Contains(today, c => c.Code == "SV");
        Assert.DoesNotContain(today, c => c.Code == "SR");

        Assert.Contains(gstEra, c => c.Code == "SR");
        Assert.DoesNotContain(gstEra, c => c.Code == "SV");

        // The reclaim flag travels with the regime, which is the real difference between a
        // VAT system and a sales tax.
        Assert.True(gstEra.Single(c => c.Code == "SR").InputReclaimable);
        Assert.False(today.Single(c => c.Code == "SV").InputReclaimable);
    }

    [Fact]
    public async Task ARateThatHasBeenPostedUnder_CannotBeChanged()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 1000m, currentCode));
        await kit.Invoices.PostAsync(draft.Id);

        await using var editor = world.NewAppContext();
        var code = await editor.TaxCodes.FirstAsync(c => c.Id == currentCode);
        code.Rate = 10m;

        // Postings store the code, not the rate. Editing the rate would silently restate
        // the tax on every document that used it - including returns already filed.
        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("cannot change", ex!.GetBaseException().Message);
    }

    [Fact]
    public async Task AnUnusedRate_CanStillBeCorrected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);

        await using var editor = world.NewAppContext();
        var code = await editor.TaxCodes.FirstAsync(c => c.Id == currentCode);
        code.Rate = 9m;

        // Nothing has been posted under it, so a typo is still fixable.
        await editor.SaveChangesAsync();

        Assert.Equal(9m, (await editor.TaxCodes.AsNoTracking()
            .FirstAsync(c => c.Id == currentCode)).Rate);
    }

    [Fact]
    public async Task ACodeThatHasBeenPostedUnder_CannotBeDeleted()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        var draft = await kit.Invoices.CreateDraftAsync(Invoice(world, 100m, currentCode));
        await kit.Invoices.PostAsync(draft.Id);

        await using var editor = world.NewAppContext();
        editor.TaxCodes.Remove(await editor.TaxCodes.FirstAsync(c => c.Id == currentCode));

        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("cannot be deleted", ex!.GetBaseException().Message);
    }

    [Fact]
    public async Task TaxIsRoundedPerLine_AndThePostingSumsTheLines()
    {
        var world = await LedgerFixture.CreateAsync();
        var (currentCode, _, _) = await SeedRegimesAsync(world);
        await using var kit = KitFor(world);

        // 8% of 33.33 is 2.6664, rounding to 2.67. Three such lines give 8.01, whereas 8%
        // of 99.99 is 7.9992 which rounds to 8.00. The entry must use 8.01 or it will not
        // balance - which is why the rule sums the lines rather than recomputing.
        var draft = await kit.Invoices.CreateDraftAsync(new CreateSalesInvoiceRequest(
            world.EntityId, world.CustomerId, August,
            [
                new CreateSalesInvoiceLineRequest("A", 1m, 33.33m, world.SalesAccountId, TaxCodeId: currentCode),
                new CreateSalesInvoiceLineRequest("B", 1m, 33.33m, world.SalesAccountId, TaxCodeId: currentCode),
                new CreateSalesInvoiceLineRequest("C", 1m, 33.33m, world.SalesAccountId, TaxCodeId: currentCode),
            ]));

        // 8% of 33.33 is 2.6664 â†’ 2.67 per line, so 8.01 in total. Recomputing 8% of the
        // 99.99 net would give 8.00, and the entry would fail to balance by a cent.
        Assert.Equal(99.99m, draft.Total);
        Assert.Equal(8.01m, draft.TaxTotal);
        Assert.Equal(108.00m, draft.TotalWithTax);

        var posted = await kit.Invoices.PostAsync(draft.Id);
        var entry = await kit.Postings.GetAsync(posted.JournalEntryId!.Value);

        var receivable = entry.Lines.Single(l => l.AccountId == world.ReceivablesAccountId);
        Assert.Equal(108.00m, receivable.FunctionalAmount);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        Assert.True(trialBalance.IsBalanced);
    }
}
