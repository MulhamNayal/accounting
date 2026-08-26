using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

[Collection(nameof(DatabaseCollection))]
public class ConsolidationTests
{
    private static readonly DateOnly August = new(2026, 8, 15);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private sealed record Kit(
        IConsolidationService Consolidation,
        IExchangeRateService Rates,
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
        var tenant = new TenantContext();
        tenant.SetTenant(world.TenantId);
        var numbers = new NumberSeriesService(db);
        var postings = new PostingService(db, user, numbers, NullLogger<PostingService>.Instance);
        return new Kit(
            new ConsolidationService(db, user, tenant, NullLogger<ConsolidationService>.Instance),
            new ExchangeRateService(db, tenant),
            postings,
            db);
    }

    /// <summary>
    /// Posts a balanced entry, optionally marked as being with a sister entity.
    /// </summary>
    /// <remarks>
    /// The marking goes on at posting time because it has to: postings are immutable, so
    /// there is no marking a transaction as intercompany afterwards. That is the right
    /// constraint — it is a statement about what happened, not something to infer later.
    /// </remarks>
    private static async Task PostAsync(
        Kit kit, LedgerWorld world, decimal amount, Guid? intercompanyWith = null)
        => await kit.Postings.PostAsync(new PostJournalEntryRequest(
            world.EntityId, August,
            [
                new PostingLineRequest(
                    world.CashAccountId, "Debit", amount,
                    IntercompanyEntityId: intercompanyWith),
                new PostingLineRequest(
                    world.SalesAccountId, "Credit", amount,
                    IntercompanyEntityId: intercompanyWith),
            ]));

    /// <summary>A second entity in the same tenant, with its own functional currency.</summary>
    private static async Task<Guid> AddEntityAsync(
        LedgerWorld world, string code, string currency)
    {
        await using var db = world.NewAppContext();
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = code,
            Name = $"{code} Company",
            FunctionalCurrency = currency,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private static async Task AddReserveAsync(LedgerWorld world)
    {
        await using var db = world.NewAppContext();
        db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = "3030",
            Name = "Currency Translation Reserve",
            AccountType = AccountType.Equity,
            SystemRole = AccountSystemRole.CurrencyTranslationReserve,
        });
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- same currency

    [Fact]
    public async Task SingleEntity_SameCurrency_ConsolidatesToItsOwnBalances()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 1000m);

        var result = await kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "MYR"));

        Assert.True(result.IsBalanced);

        var cash = result.Lines.Single(l => l.AccountId == world.CashAccountId);
        Assert.Equal(1000m, cash.EntityTotal);
        Assert.Equal(0m, cash.Eliminations);
        Assert.Equal(1000m, cash.Consolidated);
    }

    [Fact]
    public async Task Consolidation_IsStoredAndReadableAfterwards()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 500m);
        var run = await kit.Consolidation.RunAsync(
            new RunConsolidationRequest(YearEnd, "MYR", "Board pack"));

        // Kept rather than recomputed, so a published statement stays reproducible.
        var reread = await kit.Consolidation.GetAsync(run.Id);

        Assert.Equal("Board pack", reread.Note);
        Assert.Equal(run.TotalDebit, reread.TotalDebit);
        Assert.Contains(await kit.Consolidation.ListAsync(), r => r.Id == run.Id);
    }

    [Fact]
    public async Task AConsolidation_CannotBeAlteredAfterwards()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 100m);
        var run = await kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "MYR"));

        await using var editor = world.NewAppContext();
        var line = await editor.ConsolidationPostings.FirstAsync(p => p.ConsolidationRunId == run.Id);
        line.PresentationAmount = 999_999m;

        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("permission denied", ex!.GetBaseException().Message);
    }

    // ---------------------------------------------------------------- eliminations

    [Fact]
    public async Task IntercompanyPostings_AreEliminatedFromTheGroupFigure()
    {
        var world = await LedgerFixture.CreateAsync();
        var sister = await AddEntityAsync(world, "SIS", "MYR");
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 1000m);                          // real third-party trade
        await PostAsync(kit, world, 250m, intercompanyWith: sister); // within the group

        var result = await kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "MYR"));

        var sales = result.Lines.Single(l => l.AccountId == world.SalesAccountId);

        // Entities recorded 1250; 250 of it was the group trading with itself.
        Assert.Equal(-1250m, sales.EntityTotal);
        Assert.Equal(250m, sales.Eliminations);
        Assert.Equal(-1000m, sales.Consolidated);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task ThirdPartyTrade_IsNeverEliminated()
    {
        var world = await LedgerFixture.CreateAsync();
        await AddEntityAsync(world, "SIS", "MYR");
        await using var kit = KitFor(world);

        // Same amount as an intercompany posting would be. Elimination follows the marking,
        // not a coincidence of figures.
        await PostAsync(kit, world, 250m);

        var result = await kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "MYR"));
        var sales = result.Lines.Single(l => l.AccountId == world.SalesAccountId);

        Assert.Equal(0m, sales.Eliminations);
        Assert.Equal(-250m, sales.Consolidated);
    }

    [Fact]
    public async Task Intercompany_IsReportableBeforeConsolidating()
    {
        var world = await LedgerFixture.CreateAsync();
        var sister = await AddEntityAsync(world, "SIS", "MYR");
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 400m, intercompanyWith: sister);

        var pairs = await kit.Consolidation.GetIntercompanyAsync(YearEnd);
        var pair = pairs.Single();

        Assert.Equal("TEST", pair.FromEntity);
        Assert.Equal("SIS", pair.ToEntity);
        // Both sides of this entry sit in one entity, so they net to nothing. A non-zero
        // figure here would mean one entity recorded something its counterpart did not.
        Assert.Equal(0m, pair.NetBalance);
        Assert.Equal(2, pair.PostingCount);
    }

    // ---------------------------------------------------------------- translation

    [Fact]
    public async Task ForeignEntity_WithoutARate_IsRefusedRatherThanGuessed()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 100m);

        // The entity keeps MYR; the group wants SGD, and no rate exists.
        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "SGD")));

        // Defaulting to 1 would report a foreign entity as though its currency were the
        // group's — a wrong number that looks entirely plausible.
        Assert.Contains("No exchange rate", ex.Message);
    }

    [Fact]
    public async Task ForeignEntity_WithOnlyAClosingRate_IsRefusedForIncomeAndExpense()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 100m);
        await kit.Rates.UpsertAsync(new UpsertExchangeRateRequest("MYR", "SGD", August, 0.30m));

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "SGD")));

        Assert.Contains("average rate", ex.Message);
    }

    [Fact]
    public async Task ForeignEntity_IsTranslatedAndTheResidueGoesToTheReserve()
    {
        var world = await LedgerFixture.CreateAsync();
        await AddReserveAsync(world);
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 1000m);

        // Balance sheet at 0.30, income statement at 0.28. The two rates differing is
        // exactly what produces a residue.
        await kit.Rates.UpsertAsync(
            new UpsertExchangeRateRequest("MYR", "SGD", August, 0.30m, 0.28m));

        var result = await kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "SGD"));

        var cash = result.Lines.Single(l => l.AccountId == world.CashAccountId);
        var sales = result.Lines.Single(l => l.AccountId == world.SalesAccountId);

        Assert.Equal(300m, cash.Consolidated);    // 1000 asset at the closing rate
        Assert.Equal(-280m, sales.Consolidated);  // 1000 income at the average rate

        // 300 against 280 cannot balance, and that is not an error — IAS 21 takes the
        // difference to a reserve in equity rather than to profit, because nobody realised
        // a gain; the rates simply moved.
        var reserve = result.Lines.Single(l => l.AccountCode == "3030");
        Assert.Equal(-20m, reserve.Translation);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task TranslationWithNoReserveAccount_IsRefusedWithAnExplanation()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await PostAsync(kit, world, 1000m);
        await kit.Rates.UpsertAsync(
            new UpsertExchangeRateRequest("MYR", "SGD", August, 0.30m, 0.28m));

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "SGD")));

        Assert.Contains("currency translation reserve", ex.Message);
    }

    // ---------------------------------------------------------------- rates

    [Fact]
    public async Task ARate_CanBeCorrectedWithoutRestatingAnything()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await kit.Rates.UpsertAsync(new UpsertExchangeRateRequest("MYR", "SGD", August, 0.30m, 0.28m));
        var corrected = await kit.Rates.UpsertAsync(
            new UpsertExchangeRateRequest("MYR", "SGD", August, 0.31m, 0.29m, "Bank Negara"));

        Assert.Equal(0.31m, corrected.ClosingRate);
        Assert.Single(await kit.Rates.ListAsync());

        // Unlike a posting, a rate is a reference figure and nothing recorded depends on it:
        // postings store the rate they were made at, and a consolidation stores its own
        // translated lines.
        Assert.Equal("Bank Negara", corrected.Source);
    }

    [Fact]
    public async Task ARateAgainstItself_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Rates.UpsertAsync(new UpsertExchangeRateRequest("MYR", "MYR", August, 1m)));
    }

    [Fact]
    public async Task ANonPositiveRate_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Rates.UpsertAsync(new UpsertExchangeRateRequest("MYR", "SGD", August, 0m)));
    }

    [Fact]
    public async Task Consolidating_WithNoPresentationCurrency_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Consolidation.RunAsync(new RunConsolidationRequest(YearEnd, "")));
    }
}
