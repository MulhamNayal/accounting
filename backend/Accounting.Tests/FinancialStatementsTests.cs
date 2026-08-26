using Accounting.Api.Data;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Tests;

/// <summary>
/// The profit and loss account and the balance sheet, both derived from postings.
/// </summary>
/// <remarks>
/// The test that matters most is <see cref="GetBalanceSheetAsync_AlwaysBalances"/>. Assets
/// equalling liabilities plus equity is not a feature of the report -- it is a consequence of
/// double entry, so if the report can be made not to balance then the report is wrong.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class FinancialStatementsTests
{
    // The fixture's entity has a 2026 fiscal year with an open August period.
    private static readonly DateOnly InAugust2026 = new(2026, 8, 15);

    [Fact]
    public async Task GetProfitAndLossAsync_SignsIncomeAndExpensesToReadPositive()
    {
        var world = await LedgerFixture.CreateAsync();
        var extra = await AddStatementAccountsAsync(world);

        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 1000m),
            (world.SalesAccountId, PostingDirection.Credit, 1000m));

        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (extra.RentExpenseId, PostingDirection.Debit, 400m),
            (world.CashAccountId, PostingDirection.Credit, 400m));

        var pl = await StatementsAsync(world).GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(1000m, pl.Income.Total);
        Assert.Equal(400m, pl.Expenses.Total);
        Assert.Equal(600m, pl.NetProfit);
        Assert.All(pl.Income.Lines, l => Assert.True(l.Amount > 0));
        Assert.All(pl.Expenses.Lines, l => Assert.True(l.Amount > 0));
    }

    [Fact]
    public async Task GetProfitAndLossAsync_ExcludesActivityOutsideTheRange()
    {
        var world = await LedgerFixture.CreateAsync();

        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 250m),
            (world.SalesAccountId, PostingDirection.Credit, 250m));

        var statements = StatementsAsync(world);

        var including = await statements.GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var excluding = await statements.GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(250m, including.Income.Total);
        Assert.Equal(0m, excluding.Income.Total);
        Assert.Empty(excluding.Income.Lines);
    }

    [Fact]
    public async Task GetProfitAndLossAsync_RangeEndingBeforeItStarts_Throws()
    {
        var world = await LedgerFixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StatementsAsync(world).GetProfitAndLossAsync(
                world.EntityId, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public async Task GetProfitAndLossAsync_OmitsAccountsThatNetToZero()
    {
        var world = await LedgerFixture.CreateAsync();

        // Recorded and then reversed within the range: the account saw activity, but it
        // cancelled out and is noise on a statement.
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 300m),
            (world.SalesAccountId, PostingDirection.Credit, 300m));
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.SalesAccountId, PostingDirection.Debit, 300m),
            (world.CashAccountId, PostingDirection.Credit, 300m));

        var pl = await StatementsAsync(world).GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(0m, pl.Income.Total);
        Assert.Empty(pl.Income.Lines);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_AlwaysBalances()
    {
        var world = await LedgerFixture.CreateAsync();
        var extra = await AddStatementAccountsAsync(world);

        // Capital in, a sale, an expense, and a liability -- one of each section.
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 5000m),
            (extra.ShareCapitalId, PostingDirection.Credit, 5000m));
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 1200m),
            (world.SalesAccountId, PostingDirection.Credit, 1200m));
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (extra.RentExpenseId, PostingDirection.Debit, 700m),
            (extra.AccrualsId, PostingDirection.Credit, 700m));

        var sheet = await StatementsAsync(world).GetBalanceSheetAsync(
            world.EntityId, new DateOnly(2026, 8, 31));

        Assert.Equal(6200m, sheet.Assets.Total);          // 5000 capital + 1200 sale
        Assert.Equal(700m, sheet.Liabilities.Total);      // accrual
        Assert.Equal(5000m, sheet.Equity.Total);          // share capital
        Assert.Equal(500m, sheet.ResultForThePeriod);     // 1200 income - 700 expense
        Assert.Equal(0m, sheet.RetainedEarningsBroughtForward);
        Assert.Equal(5500m, sheet.TotalEquity);
        Assert.Equal(6200m, sheet.TotalLiabilitiesAndEquity);
        Assert.True(sheet.IsBalanced);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_ProfitFromAnEarlierYear_IsBroughtForward()
    {
        var world = await LedgerFixture.CreateAsync();
        var extra = await AddStatementAccountsAsync(world);
        var lastYear = await AddOpenPeriodAsync(world, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30));

        // Earned last financial year.
        await PostAsync(world, lastYear, new DateOnly(2025, 6, 15),
            (world.CashAccountId, PostingDirection.Debit, 900m),
            (world.SalesAccountId, PostingDirection.Credit, 900m));

        // And this one.
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (extra.RentExpenseId, PostingDirection.Debit, 100m),
            (world.CashAccountId, PostingDirection.Credit, 100m));

        var sheet = await StatementsAsync(world).GetBalanceSheetAsync(
            world.EntityId, new DateOnly(2026, 8, 31));

        Assert.Equal(900m, sheet.RetainedEarningsBroughtForward);
        Assert.Equal(-100m, sheet.ResultForThePeriod);
        Assert.Equal(800m, sheet.TotalEquity);
        Assert.True(sheet.IsBalanced);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_AgreesWithTheTrialBalance()
    {
        var world = await LedgerFixture.CreateAsync();
        var extra = await AddStatementAccountsAsync(world);

        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (world.CashAccountId, PostingDirection.Debit, 2000m),
            (extra.ShareCapitalId, PostingDirection.Credit, 2000m));
        await PostAsync(world, world.OpenPeriodId, InAugust2026,
            (extra.RentExpenseId, PostingDirection.Debit, 350m),
            (world.CashAccountId, PostingDirection.Credit, 350m));

        var asOf = new DateOnly(2026, 8, 31);
        var sheet = await StatementsAsync(world).GetBalanceSheetAsync(world.EntityId, asOf);

        await using var db = world.NewAppContext();
        var trial = await ReadOnlyPostings(db, world.UserId).GetTrialBalanceAsync(world.EntityId, asOf);

        // The trial balance nets to zero across every account; the balance sheet is the same
        // numbers rearranged, so assets must equal the other side exactly.
        Assert.True(trial.IsBalanced);
        Assert.Equal(sheet.Assets.Total, sheet.TotalLiabilitiesAndEquity);

        var trialAssets = trial.Lines
            .Where(l => l.AccountType == nameof(AccountType.Asset))
            .Sum(l => l.Balance);
        Assert.Equal(trialAssets, sheet.Assets.Total);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_WithNoPostings_BalancesAtZero()
    {
        var world = await LedgerFixture.CreateAsync();

        var sheet = await StatementsAsync(world).GetBalanceSheetAsync(
            world.EntityId, new DateOnly(2026, 8, 31));

        Assert.Equal(0m, sheet.Assets.Total);
        Assert.Equal(0m, sheet.TotalLiabilitiesAndEquity);
        Assert.True(sheet.IsBalanced);
        Assert.Equal("MYR", sheet.CurrencyCode);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_UnknownEntity_Throws()
    {
        var world = await LedgerFixture.CreateAsync();

        await Assert.ThrowsAsync<Api.Exceptions.NotFoundException>(() =>
            StatementsAsync(world).GetBalanceSheetAsync(Guid.NewGuid(), InAugust2026));
    }

    // ---------------------------------------------------------------- helpers

    private static FinancialStatementsService StatementsAsync(LedgerWorld world) =>
        new(world.NewAppContext());

    private sealed record ExtraAccounts(Guid RentExpenseId, Guid ShareCapitalId, Guid AccrualsId);

    /// <summary>
    /// The shared fixture has no expense, liability or equity account, and adding them there
    /// would touch every other test in the suite for no benefit.
    /// </summary>
    private static async Task<ExtraAccounts> AddStatementAccountsAsync(LedgerWorld world)
    {
        await using var db = world.NewAppContext();

        var rent = NewAccount(world.TenantId, "6100", "Rent", AccountType.Expense);
        var capital = NewAccount(world.TenantId, "3010", "Share Capital", AccountType.Equity);
        var accruals = NewAccount(world.TenantId, "2100", "Accruals", AccountType.Liability);

        db.Accounts.AddRange(rent, capital, accruals);
        await db.SaveChangesAsync();

        return new ExtraAccounts(rent.Id, capital.Id, accruals.Id);
    }

    private static Account NewAccount(Guid tenantId, string code, string name, AccountType type) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Code = code,
        Name = name,
        AccountType = type,
    };

    /// <summary>
    /// A fiscal year and one open period covering the given dates, so an entry can be posted
    /// outside the fixture's 2026 year.
    /// </summary>
    private static async Task<Guid> AddOpenPeriodAsync(LedgerWorld world, DateOnly start, DateOnly end)
    {
        await using var db = world.NewAppContext();

        var fiscalYearId = Guid.NewGuid();
        db.FiscalYears.Add(new FiscalYear
        {
            Id = fiscalYearId,
            TenantId = world.TenantId,
            LegalEntityId = world.EntityId,
            Code = $"FY{start.Year}",
            StartDate = new DateOnly(start.Year, 1, 1),
            EndDate = new DateOnly(start.Year, 12, 31),
            State = PeriodState.Open,
        });

        var periodId = Guid.NewGuid();
        db.Periods.Add(new AccountingPeriod
        {
            Id = periodId,
            TenantId = world.TenantId,
            LegalEntityId = world.EntityId,
            FiscalYearId = fiscalYearId,
            Sequence = start.Month,
            StartDate = start,
            EndDate = end,
            State = PeriodState.Open,
        });

        await db.SaveChangesAsync();
        return periodId;
    }

    private static async Task PostAsync(
        LedgerWorld world,
        Guid periodId,
        DateOnly date,
        params (Guid AccountId, PostingDirection Direction, decimal Amount)[] lines)
    {
        await using var db = world.NewAppContext();

        var entryId = Guid.NewGuid();
        var entry = new JournalEntry
        {
            Id = entryId,
            TenantId = world.TenantId,
            LegalEntityId = world.EntityId,
            EntryNo = $"JV-{Guid.NewGuid():N}"[..12],
            EntryDate = date,
            PeriodId = periodId,
            SourceDocumentType = "Manual",
            PostedAtUtc = DateTimeOffset.UtcNow,
            PostedByUserId = world.UserId,
        };

        var lineNo = 1;
        foreach (var (accountId, direction, amount) in lines)
        {
            entry.Postings.Add(new Posting
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                LegalEntityId = world.EntityId,
                JournalEntryId = entryId,
                LineNo = lineNo++,
                AccountId = accountId,
                Direction = direction,
                Amount = amount,
                CurrencyCode = "MYR",
                FunctionalAmount = amount,
                FxRate = 1m,
            });
        }

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A posting service good enough to read a trial balance from. The number series is null
    /// because this only ever reads; allocating a number is the one thing it will not do.
    /// </summary>
    private static PostingService ReadOnlyPostings(AccountingDbContext db, Guid userId)
    {
        var user = new CurrentUser();
        user.SetUser(userId);
        return new PostingService(
            db, user, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PostingService>.Instance);
    }
}
