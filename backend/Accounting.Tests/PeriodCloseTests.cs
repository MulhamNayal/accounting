using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accounting.Tests;

/// <summary>
/// Period close, year-end close, and the guarantees PostgreSQL makes about both.
/// </summary>
/// <remarks>
/// The tests that matter most are the two that bypass the services entirely --
/// <see cref="ChangingStateWithNoEvent_IsRefusedByTheDatabase"/> and
/// <see cref="ReopeningAHardClosedPeriod_IsRefusedByTheDatabase"/>. The design's claim is
/// that the period trail cannot be skipped and that a hard close cannot be undone. If those
/// hold only because no service method does it, they are conventions rather than guarantees.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class PeriodCloseTests
{
    private const string Reason = "Month-end close";

    // The fixture's entity has an open August 2026 period and a hard closed January one.
    private static readonly DateOnly InAugust2026 = new(2026, 8, 15);

    // ---------------------------------------------------------------- provisioning

    [Fact]
    public async Task CreateAsync_WithNoPeriodCount_GeneratesCalendarMonths()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));

        Assert.Equal(12, year.PeriodCount);
        Assert.Equal(12, year.OpenPeriodCount);

        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);
        Assert.Equal(new DateOnly(2027, 1, 1), periods[0].StartDate);
        Assert.Equal(new DateOnly(2027, 1, 31), periods[0].EndDate);
        Assert.Equal(new DateOnly(2027, 2, 28), periods[1].EndDate);
        Assert.Equal(new DateOnly(2027, 12, 31), periods[^1].EndDate);
    }

    [Fact]
    public async Task CreateAsync_StartingMidMonth_MakesTheFirstPeriodShort()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 4, 15), new DateOnly(2028, 3, 31)));

        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);

        Assert.Equal(new DateOnly(2027, 4, 15), periods[0].StartDate);
        Assert.Equal(new DateOnly(2027, 4, 30), periods[0].EndDate);
        Assert.Equal(new DateOnly(2028, 3, 31), periods[^1].EndDate);
    }

    [Fact]
    public async Task CreateAsync_WithAPeriodCount_DividesTheYearEvenly()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            PeriodCount: 13));

        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);

        Assert.Equal(13, periods.Count);
        Assert.Equal(new DateOnly(2027, 1, 1), periods[0].StartDate);
        Assert.Equal(new DateOnly(2027, 12, 31), periods[^1].EndDate);

        // No gaps and no overlaps: each period starts the day after the previous one ended.
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.Equal(periods[i - 1].EndDate.AddDays(1), periods[i].StartDate);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryYearNewestFirst()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));

        var years = await services.Years.ListAsync(world.EntityId);

        // The fixture's own FY2026 plus the one just created.
        Assert.Equal(2, years.Count);
        Assert.Equal("FY2027", years[0].Code);
        Assert.Equal("FY2026", years[1].Code);
        Assert.Equal(12, years[0].PeriodCount);
        Assert.Null(years[0].ClosingEntryId);
    }

    [Fact]
    public async Task ListAsync_ReportsTheClosingEntryOncePosted()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var entry = await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var listed = (await services.Years.ListAsync(world.EntityId)).Single(y => y.Id == year.Id);

        Assert.Equal(entry.EntryNo, listed.ClosingEntryNo);
        Assert.False(listed.ClosingEntryIsReversed);
    }

    [Fact]
    public async Task CreateAsync_OverlappingAnExistingYear_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        // The fixture already has FY2026 running the whole of 2026.
        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Years.CreateAsync(new CreateFiscalYearRequest(
                world.EntityId, "FY2026B", new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31))));

        Assert.Contains("overlaps", error.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Years.CreateAsync(new CreateFiscalYearRequest(
                world.EntityId, "FY2026", new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31))));

        Assert.Contains("FY2026", error.Message);
    }

    // ---------------------------------------------------------------- soft close

    [Fact]
    public async Task SoftCloseAsync_RecordsAnEventAndStopsPosting()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var closed = await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        Assert.Equal(nameof(PeriodState.SoftClosed), closed.State);

        var events = await services.Periods.GetEventsAsync(world.EntityId, null);
        var recorded = Assert.Single(events);
        Assert.Equal(nameof(PeriodState.Open), recorded.FromState);
        Assert.Equal(nameof(PeriodState.SoftClosed), recorded.ToState);
        Assert.Equal(Reason, recorded.Reason);
        Assert.Equal("Test User", recorded.ByUser);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Posting.PostAsync(SimpleEntry(world, InAugust2026)));

        Assert.Contains("does not accept postings", error.Message);
    }

    [Fact]
    public async Task SoftCloseAsync_WithoutAReason_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Periods.SoftCloseAsync(world.OpenPeriodId, "   "));
    }

    [Fact]
    public async Task SoftCloseAsync_WithAnEarlierPeriodStillOpen_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));

        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Periods.SoftCloseAsync(periods[1].Id, Reason));

        Assert.Contains("still open", error.Message);
        Assert.Contains("close in order", error.Message);
    }

    [Fact]
    public async Task SoftCloseAsync_AlreadyClosed_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason));

        Assert.Contains("already closed", error.Message);
    }

    // ---------------------------------------------------------------- reopen

    [Fact]
    public async Task ReopenAsync_AnEarlierPeriod_IsAllowedWhileLaterOnesStayClosed()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)));
        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);

        await services.Periods.SoftCloseAsync(periods[0].Id, Reason);
        await services.Periods.SoftCloseAsync(periods[1].Id, Reason);

        // Reopening January while February stays closed is safe here because every balance
        // is derived from postings — there is no stored opening balance to invalidate.
        var reopened = await services.Periods.ReopenAsync(periods[0].Id, "Late supplier bill");

        Assert.Equal(nameof(PeriodState.Open), reopened.State);

        var after = await services.Periods.ListAsync(world.EntityId, year.Id);
        Assert.Equal(nameof(PeriodState.SoftClosed), after[1].State);

        // And the trail carries both directions, with the reason given for each.
        var events = await services.Periods.GetEventsAsync(world.EntityId, year.Id);
        Assert.Contains(events, e =>
            e.ToState == nameof(PeriodState.Open) && e.Reason == "Late supplier bill");
    }

    [Fact]
    public async Task ReopenAsync_AHardClosedPeriod_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Periods.ReopenAsync(world.ClosedPeriodId, "Found an error"));

        Assert.Contains("hard closed", error.Message);
    }

    // ---------------------------------------------------------------- the database's own rules

    [Fact]
    public async Task ChangingStateWithNoEvent_IsRefusedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();

        await using var db = world.NewAppContext();
        var period = await db.Periods.FirstAsync(p => p.Id == world.OpenPeriodId);

        // Exactly what a bug, or a support engineer in a hurry, would do.
        period.State = PeriodState.SoftClosed;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("nothing recorded in period_events", error.GetBaseException().Message);
    }

    [Fact]
    public async Task ChangingStateWithAStaleEvent_IsRefusedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        // A genuine close, properly recorded, then a genuine reopen.
        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);
        await services.Periods.ReopenAsync(world.OpenPeriodId, "Adjustment needed");

        // Now close it again, leaning on the first close's event as evidence.
        await using var db = world.NewAppContext();
        var period = await db.Periods.FirstAsync(p => p.Id == world.OpenPeriodId);
        period.State = PeriodState.SoftClosed;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("most recent recorded event", error.GetBaseException().Message);
    }

    [Fact]
    public async Task ReopeningAHardClosedPeriod_IsRefusedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();

        await using var db = world.NewAppContext();
        var period = await db.Periods.FirstAsync(p => p.Id == world.ClosedPeriodId);

        // With a matching event written, so the only thing left to refuse it is terminality.
        db.PeriodEvents.Add(new PeriodEvent
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            PeriodId = period.Id,
            FromState = PeriodState.HardClosed,
            ToState = PeriodState.Open,
            AtUtc = DateTimeOffset.UtcNow,
            ByUserId = world.UserId,
            Reason = "Trying it on",
        });

        period.State = PeriodState.Open;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("no transition out of it", error.GetBaseException().Message);
    }

    [Fact]
    public async Task MovingTheDatesOfAPeriodWithPostings_IsRefusedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Posting.PostAsync(SimpleEntry(world, InAugust2026));

        await using var db = world.NewAppContext();
        var period = await db.Periods.FirstAsync(p => p.Id == world.OpenPeriodId);
        period.EndDate = new DateOnly(2026, 9, 30);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        Assert.Contains("dates and ownership are fixed", error.GetBaseException().Message);
    }

    [Fact]
    public async Task DeletingAPeriod_IsRefusedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();

        await using var db = world.NewAppContext();
        var period = await db.Periods.FirstAsync(p => p.Id == world.ClosedPeriodId);
        db.Periods.Remove(period);

        // Deleting a hard closed period and re-inserting it as open would make terminality
        // decorative, so the privilege is revoked rather than merely unused.
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }

    // ---------------------------------------------------------------- readiness

    [Fact]
    public async Task GetReadinessAsync_ListsDraftsWithoutBlockingTheClose()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await AddDraftInvoiceAsync(world, InAugust2026);

        var readiness = await services.Periods.GetReadinessAsync(world.OpenPeriodId);

        Assert.True(readiness.CanSoftClose);
        Assert.Empty(readiness.Blockers);
        Assert.Equal(1, readiness.DraftCount);
        Assert.Equal("Sales invoices", Assert.Single(readiness.Drafts).DocumentType);
    }

    [Fact]
    public async Task GetReadinessAsync_CountsPostedEntriesAndReportsBlockers()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);

        await services.Posting.PostAsync(SimpleEntry(world, InAugust2026));
        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        var readiness = await services.Periods.GetReadinessAsync(world.OpenPeriodId);

        Assert.Equal(1, readiness.PostedEntryCount);
        Assert.False(readiness.CanSoftClose);
        Assert.Contains(readiness.Blockers, b => b.Contains("already closed"));
    }

    // ---------------------------------------------------------------- year-end close

    [Fact]
    public async Task PostClosingEntryAsync_ZeroesTheProfitAndLossAccounts()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var entry = await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        Assert.Equal("YearEndClose", entry.SourceDocumentType);
        Assert.Equal(new DateOnly(2027, 12, 31), entry.EntryDate);

        var trial = await services.Posting.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2027, 12, 31));

        var sales = trial.Lines.Single(l => l.AccountId == world.SalesAccountId);
        Assert.Equal(0m, sales.Balance);

        var retained = trial.Lines.Single(l => l.AccountCode == "3020");
        Assert.Equal(-1000m, retained.Balance);   // a credit balance, in debit-positive terms
    }

    [Fact]
    public async Task PostClosingEntryAsync_LeavesTheProfitAndLossAccountReportingTheYear()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var statements = new FinancialStatementsService(services.Db);

        var before = await statements.GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var after = await statements.GetProfitAndLossAsync(
            world.EntityId, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // The whole reason closing entries are marked. Without the exclusion this reads zero.
        Assert.Equal(1000m, before.Income.Total);
        Assert.Equal(1000m, after.Income.Total);
        Assert.Equal(before.NetProfit, after.NetProfit);
    }

    [Fact]
    public async Task PostClosingEntryAsync_LeavesTheBalanceSheetUnchangedAndBalanced()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var statements = new FinancialStatementsService(services.Db);
        var asOf = new DateOnly(2027, 12, 31);

        var before = await statements.GetBalanceSheetAsync(world.EntityId, asOf);

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var after = await statements.GetBalanceSheetAsync(world.EntityId, asOf);

        Assert.True(before.IsBalanced);
        Assert.True(after.IsBalanced);
        Assert.Equal(before.TotalEquity, after.TotalEquity);
        Assert.Equal(before.TotalLiabilitiesAndEquity, after.TotalLiabilitiesAndEquity);
    }

    [Fact]
    public async Task PostClosingEntryAsync_Twice_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.YearEnd.PostClosingEntryAsync(year.Id, null));

        Assert.Contains("already been closed off", error.Message);
    }

    [Fact]
    public async Task PostClosingEntryAsync_WithNothingPosted_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        await AddRetainedEarningsAccountAsync(world);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            PeriodCount: 1));

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.YearEnd.PostClosingEntryAsync(year.Id, null));

        Assert.Contains("nothing to close", error.Message);
    }

    [Fact]
    public async Task GetPreviewAsync_ReportsTheResultWithoutPostingAnything()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var preview = await services.YearEnd.GetPreviewAsync(year.Id);

        Assert.True(preview.CanPost);
        Assert.Equal(1000m, preview.TotalIncome);
        Assert.Equal(0m, preview.TotalExpense);
        Assert.Equal(1000m, preview.NetResult);
        Assert.Equal("3020", preview.RetainedEarningsAccountCode);
        Assert.Equal(2, preview.Lines.Count);

        // Nothing was written: asking twice gives the same answer.
        var again = await services.YearEnd.GetPreviewAsync(year.Id);
        Assert.Equal(preview.NetResult, again.NetResult);
    }

    // ---------------------------------------------------------------- finalise

    [Fact]
    public async Task FinaliseAsync_WithNoClosingEntry_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.YearEnd.FinaliseAsync(year.Id, "Filed"));

        Assert.Contains("no closing entry", error.Message);
    }

    [Fact]
    public async Task FinaliseAsync_WithAPeriodStillOpen_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.YearEnd.FinaliseAsync(year.Id, "Filed"));

        Assert.Contains("still open", error.Message);
    }

    [Fact]
    public async Task FinaliseAsync_HardClosesEveryPeriodAndLeavesNoWayBack()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        var periods = await services.Periods.ListAsync(world.EntityId, year.Id);
        foreach (var period in periods)
        {
            await services.Periods.SoftCloseAsync(period.Id, Reason);
        }

        var finalised = await services.YearEnd.FinaliseAsync(year.Id, "Filed with LHDN");

        Assert.Equal(nameof(PeriodState.HardClosed), finalised.State);
        Assert.Equal(0, finalised.OpenPeriodCount);
        Assert.False(finalised.CanFinalise);

        var after = await services.Periods.ListAsync(world.EntityId, year.Id);
        Assert.All(after, p => Assert.Equal(nameof(PeriodState.HardClosed), p.State));

        // The transition is recorded per period, with the reason carried through.
        var events = await services.Periods.GetEventsAsync(world.EntityId, year.Id);
        Assert.Contains(events, e =>
            e.ToState == nameof(PeriodState.HardClosed) && e.Reason.Contains("Filed with LHDN"));

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.Periods.ReopenAsync(after[0].Id, "Changed my mind"));

        Assert.Contains("hard closed", error.Message);
    }

    [Fact]
    public async Task FinaliseAsync_Twice_IsRefused()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        foreach (var period in await services.Periods.ListAsync(world.EntityId, year.Id))
        {
            await services.Periods.SoftCloseAsync(period.Id, Reason);
        }

        await services.YearEnd.FinaliseAsync(year.Id, "Filed");

        var error = await Assert.ThrowsAsync<PostingValidationException>(() =>
            services.YearEnd.FinaliseAsync(year.Id, "Filed again"));

        Assert.Contains("already hard closed", error.Message);
    }

    [Fact]
    public async Task ReverseAsync_OfAClosingEntry_LetsTheYearBeClosedAgain()
    {
        var world = await LedgerFixture.CreateAsync();
        var services = ServicesFor(world);
        var year = await PrepareClosableYearAsync(world, services);

        var entry = await services.YearEnd.PostClosingEntryAsync(year.Id, null);

        // A reversal is dated today, not on the original's date, so today's period has to be
        // open for one to be posted at all. The setup closed it to satisfy sequential closing.
        await services.Periods.ReopenAsync(world.OpenPeriodId, "Reversing the year-end entry");

        await services.Posting.ReverseAsync(entry.Id, "Late adjustment found");

        // The reversal carries the mark, so recomputing reads the year's trading rather than
        // the reversal's amounts.
        var preview = await services.YearEnd.GetPreviewAsync(year.Id);

        Assert.True(preview.CanPost);
        Assert.Equal(1000m, preview.TotalIncome);

        var second = await services.YearEnd.PostClosingEntryAsync(year.Id, null);
        Assert.NotEqual(entry.Id, second.Id);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Services(
        PeriodService Periods,
        FiscalYearService Years,
        YearEndCloseService YearEnd,
        PostingService Posting,
        AccountingDbContext Db);

    private static Services ServicesFor(LedgerWorld world)
    {
        var db = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);

        var posting = new PostingService(
            db, user, new NumberSeriesService(db), NullLogger<PostingService>.Instance,
            FixedClock.InsideTheOpenPeriod);
        var years = new FiscalYearService(db, NullLogger<FiscalYearService>.Instance);

        return new Services(
            new PeriodService(db, user, NullLogger<PeriodService>.Instance),
            years,
            new YearEndCloseService(
                db, posting, user, years, NullLogger<YearEndCloseService>.Instance),
            posting,
            db);
    }

    private static PostJournalEntryRequest SimpleEntry(LedgerWorld world, DateOnly date) => new(
        world.EntityId,
        date,
        [
            new PostingLineRequest(world.CashAccountId, "Debit", 1000m),
            new PostingLineRequest(world.SalesAccountId, "Credit", 1000m),
        ]);

    /// <summary>
    /// A single-period FY2027 with 1000 of income posted into it, ready to be closed off.
    /// </summary>
    /// <remarks>
    /// One period rather than twelve so that finalising does not need a dozen closes to set
    /// up, and because a whole-year period exercises the explicit period count as well. The
    /// fixture's own August 2026 period is closed first: closing runs in sequence, so an
    /// earlier open period would block FY2027's.
    /// </remarks>
    private static async Task<FiscalYearSummary> PrepareClosableYearAsync(
        LedgerWorld world, Services services)
    {
        await AddRetainedEarningsAccountAsync(world);
        await services.Periods.SoftCloseAsync(world.OpenPeriodId, Reason);

        var year = await services.Years.CreateAsync(new CreateFiscalYearRequest(
            world.EntityId, "FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            PeriodCount: 1));

        await services.Posting.PostAsync(SimpleEntry(world, new DateOnly(2027, 6, 30)));

        return year;
    }

    /// <summary>
    /// The shared fixture has no retained earnings account, and adding one there would touch
    /// every other test in the suite for no benefit.
    /// </summary>
    private static async Task AddRetainedEarningsAccountAsync(LedgerWorld world)
    {
        await using var db = world.NewAppContext();

        db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            Code = "3020",
            Name = "Retained Earnings",
            AccountType = AccountType.Equity,
            IsPostable = true,
            SystemRole = AccountSystemRole.RetainedEarnings,
        });

        await db.SaveChangesAsync();
    }

    private static async Task AddDraftInvoiceAsync(LedgerWorld world, DateOnly docDate)
    {
        await using var db = world.NewAppContext();

        db.SalesInvoices.Add(new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = world.TenantId,
            LegalEntityId = world.EntityId,
            DocDate = docDate,
            DueDate = docDate.AddDays(30),
            CustomerId = world.CustomerId,
            CurrencyCode = "MYR",
            FxRate = 1m,
            State = DocumentState.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = world.UserId,
        });

        await db.SaveChangesAsync();
    }
}
