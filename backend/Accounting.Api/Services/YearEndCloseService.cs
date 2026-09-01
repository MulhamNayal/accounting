using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Api.Services;

public interface IYearEndCloseService
{
    Task<ClosingEntryPreview> GetPreviewAsync(Guid fiscalYearId, CancellationToken ct = default);

    Task<JournalEntryDetail> PostClosingEntryAsync(
        Guid fiscalYearId, string? memo, CancellationToken ct = default);

    Task<FiscalYearSummary> FinaliseAsync(
        Guid fiscalYearId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Closes a financial year: transfers the year's result to retained earnings, then — as a
/// separate act — makes the year final.
/// </summary>
/// <remarks>
/// Two steps on purpose. The closing entry is an ordinary journal entry and can be reversed,
/// which is what makes a late adjustment survivable; hard closing cannot be undone anywhere
/// in this model. Collapsing them into one call would mean a mistyped year-end was
/// unrecoverable.
/// <para>
/// The order matters and the error messages say so: the entry is dated the last day of the
/// year, so that period must still be open when it is posted. In practice the sequence is
/// close the earlier months, post the closing entry, close the final month, finalise.
/// </para>
/// </remarks>
public sealed class YearEndCloseService(
    AccountingDbContext db,
    IPostingService posting,
    ICurrentUser currentUser,
    IFiscalYearService fiscalYears,
    ILogger<YearEndCloseService> logger) : IYearEndCloseService
{
    public async Task<ClosingEntryPreview> GetPreviewAsync(
        Guid fiscalYearId, CancellationToken ct = default)
    {
        var year = await FindYearAsync(fiscalYearId, ct);
        var entity = await db.LegalEntities.FirstAsync(e => e.Id == year.LegalEntityId, ct);

        var results = await YearResultsAsync(year, ct);
        var retainedEarnings = await FindRetainedEarningsAsync(year.TenantId, ct);
        var lines = BuildClosingLines(results, retainedEarnings);

        var totalIncome = results
            .Where(r => r.AccountType == AccountType.Income)
            .Sum(r => r.Credit - r.Debit);

        var totalExpense = results
            .Where(r => r.AccountType == AccountType.Expense)
            .Sum(r => r.Debit - r.Credit);

        var blockers = new List<string>();

        if (year.State == PeriodState.HardClosed)
        {
            blockers.Add($"Fiscal year {year.Code} is hard closed.");
        }

        if (retainedEarnings is null)
        {
            blockers.Add(
                "No account is marked as retained earnings, so there is nowhere to transfer "
                + "the year's result. Mark one on the chart of accounts first.");
        }

        var existing = await FindStandingClosingEntryAsync(year.Id, ct);
        if (existing is not null)
        {
            blockers.Add(
                $"Fiscal year {year.Code} has already been closed off by entry "
                + $"{existing.EntryNo}. Reverse that entry before posting another.");
        }

        blockers.AddRange(await ClosingPeriodBlockersAsync(year, ct));

        if (lines.Count == 0)
        {
            blockers.Add(
                $"There is nothing to close: no income or expense was posted in {year.Code}.");
        }

        return new ClosingEntryPreview(
            year.Id,
            year.Code,
            year.EndDate,
            entity.FunctionalCurrency,
            lines,
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            retainedEarnings?.Code ?? string.Empty,
            blockers);
    }

    public async Task<JournalEntryDetail> PostClosingEntryAsync(
        Guid fiscalYearId, string? memo, CancellationToken ct = default)
    {
        var preview = await GetPreviewAsync(fiscalYearId, ct);

        if (preview.Blockers.Count > 0)
        {
            throw new PostingValidationException(string.Join(" ", preview.Blockers));
        }

        var year = await FindYearAsync(fiscalYearId, ct);

        var entry = await posting.PostClosingEntryAsync(
            new PostClosingJournalEntryRequest(
                year.LegalEntityId,
                year.Id,
                year.EndDate,
                [.. preview.Lines.Select(l => new PostingLineRequest(
                    l.AccountId,
                    l.Direction,
                    l.Amount,
                    Description: $"Closing {year.Code}"))],
                memo ?? $"Transfer of the {year.Code} result to retained earnings"),
            ct);

        logger.LogInformation(
            "Posted closing entry {EntryNo} for {Year}, net result {Net}",
            entry.EntryNo, year.Code, preview.NetResult);

        return entry;
    }

    public async Task<FiscalYearSummary> FinaliseAsync(
        Guid fiscalYearId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PostingValidationException(
                "Finalising a year must carry a reason. It is the one transition with no way "
                + "back, so it is the one most worth explaining.");
        }

        var userId = currentUser.UserId
            ?? throw new PostingValidationException(
                "No acting user. A transition that cannot be attributed to someone must not "
                + "be recorded.");

        var year = await db.FiscalYears
            .Include(f => f.Periods)
            .FirstOrDefaultAsync(f => f.Id == fiscalYearId, ct)
            ?? throw new NotFoundException($"No fiscal year with id {fiscalYearId}.");

        if (year.State == PeriodState.HardClosed)
        {
            throw new PostingValidationException(
                $"Fiscal year {year.Code} is already hard closed.");
        }

        var closingEntry = await FindStandingClosingEntryAsync(year.Id, ct)
            ?? throw new PostingValidationException(
                $"Fiscal year {year.Code} has no closing entry. Post one first, or the year "
                + "would be frozen with its income and expenses still sitting in the profit "
                + "and loss accounts and no way to transfer them.");

        // Every month must already be closed. Hard closing an open period would skip the
        // per-period readiness check, and with it the chance to notice that the month still
        // has draft documents which will never be postable again.
        var stillOpen = year.Periods
            .Where(p => p.State == PeriodState.Open)
            .OrderBy(p => p.Sequence)
            .Select(p => p.Sequence.ToString())
            .ToList();

        if (stillOpen.Count > 0)
        {
            throw new PostingValidationException(
                $"Period{(stillOpen.Count == 1 ? "" : "s")} {string.Join(", ", stillOpen)} of "
                + $"{year.Code} {(stillOpen.Count == 1 ? "is" : "are")} still open. Close each "
                + "month before finalising the year, so nothing is frozen without being looked "
                + "at first.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;

        // The events are written here rather than through PeriodService because finalising is
        // one act on a whole year, and the per-period rules that service enforces — closing in
        // sequence, reopening freely — do not apply to a transition that has no way back.
        foreach (var period in year.Periods.Where(p => p.State != PeriodState.HardClosed))
        {
            db.PeriodEvents.Add(new PeriodEvent
            {
                Id = Guid.NewGuid(),
                TenantId = year.TenantId,
                PeriodId = period.Id,
                FromState = period.State,
                ToState = PeriodState.HardClosed,
                AtUtc = now,
                ByUserId = userId,
                Reason = $"{year.Code} finalised: {reason.Trim()}",
            });

            period.State = PeriodState.HardClosed;
        }

        year.State = PeriodState.HardClosed;

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException pg)
        {
            logger.LogWarning(ex, "The database refused a year-end finalise: {Message}", pg.MessageText);
            throw new LedgerIntegrityException(
                $"The ledger refused this year-end close: {pg.MessageText}", ex);
        }

        logger.LogInformation(
            "Finalised fiscal year {Year}: {Periods} periods hard closed, closing entry {EntryNo}",
            year.Code, year.Periods.Count, closingEntry.EntryNo);

        return await fiscalYears.GetAsync(year.Id, ct);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record AccountResult(
        Guid AccountId,
        string Code,
        string Name,
        AccountType AccountType,
        decimal Debit,
        decimal Credit);

    private async Task<FiscalYear> FindYearAsync(Guid fiscalYearId, CancellationToken ct) =>
        await db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fiscalYearId, ct)
            ?? throw new NotFoundException($"No fiscal year with id {fiscalYearId}.");

    /// <summary>
    /// Income and expense balances for the year, excluding the close's own entries.
    /// </summary>
    /// <remarks>
    /// The exclusion covers a reversal too, because <c>ReverseAsync</c> carries the mark onto
    /// the reversing entry. Without that, reversing a closing entry and computing a fresh one
    /// would read the reversal's amounts as the year's trading.
    /// </remarks>
    private async Task<List<AccountResult>> YearResultsAsync(
        FiscalYear year, CancellationToken ct) =>
        await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == year.LegalEntityId
                        && p.JournalEntry!.EntryDate >= year.StartDate
                        && p.JournalEntry.EntryDate <= year.EndDate
                        && p.JournalEntry.ClosesFiscalYearId == null
                        && (p.Account!.AccountType == AccountType.Income
                            || p.Account.AccountType == AccountType.Expense))
            .GroupBy(p => new { p.AccountId, p.Account!.Code, p.Account.Name, p.Account.AccountType })
            .Select(g => new AccountResult(
                g.Key.AccountId,
                g.Key.Code,
                g.Key.Name,
                g.Key.AccountType,
                g.Where(p => p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                g.Where(p => p.Direction == PostingDirection.Credit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m))
            .ToListAsync(ct);

    /// <summary>
    /// The lines that take every profit and loss account to nothing and put the difference in
    /// retained earnings.
    /// </summary>
    /// <remarks>
    /// Each account is closed on the side opposite its balance, so a loss-making expense
    /// account with a credit balance is debited rather than assumed away. The retained
    /// earnings line is whatever is left, which is why the entry always balances: the sum of
    /// the account lines is the year's result, and the last line is its opposite.
    /// </remarks>
    private static List<ClosingEntryLine> BuildClosingLines(
        List<AccountResult> results, Account? retainedEarnings)
    {
        var lines = new List<ClosingEntryLine>();

        foreach (var result in results.OrderBy(r => r.Code))
        {
            var balance = result.AccountType == AccountType.Income
                ? result.Credit - result.Debit
                : result.Debit - result.Credit;

            if (balance == 0)
            {
                continue;
            }

            // Income carries a credit balance and is closed by a debit; expense the reverse.
            // A negative balance flips the side, which is why this is written as a comparison
            // rather than a fixed direction per account type.
            var closesWithDebit = result.AccountType == AccountType.Income
                ? balance > 0
                : balance < 0;

            lines.Add(new ClosingEntryLine(
                result.AccountId,
                result.Code,
                result.Name,
                result.AccountType.ToString(),
                closesWithDebit ? nameof(PostingDirection.Debit) : nameof(PostingDirection.Credit),
                Math.Abs(balance)));
        }

        if (lines.Count == 0)
        {
            return lines;
        }

        if (retainedEarnings is null)
        {
            // With nowhere to put the result the entry cannot balance, and half an entry in a
            // preview reads as a whole one. The blocker explains why there is nothing here.
            return [];
        }

        var netResult =
            results.Where(r => r.AccountType == AccountType.Income).Sum(r => r.Credit - r.Debit)
            - results.Where(r => r.AccountType == AccountType.Expense).Sum(r => r.Debit - r.Credit);

        if (netResult != 0)
        {
            lines.Add(new ClosingEntryLine(
                retainedEarnings.Id,
                retainedEarnings.Code,
                retainedEarnings.Name,
                retainedEarnings.AccountType.ToString(),
                netResult > 0 ? nameof(PostingDirection.Credit) : nameof(PostingDirection.Debit),
                Math.Abs(netResult)));
        }

        return lines;
    }

    private async Task<Account?> FindRetainedEarningsAsync(Guid tenantId, CancellationToken ct) =>
        await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.TenantId == tenantId
                     && a.SystemRole == AccountSystemRole.RetainedEarnings
                     && a.IsPostable && a.IsActive,
                ct);

    /// <summary>The year's closing entry, if one stands un-reversed.</summary>
    private async Task<JournalEntry?> FindStandingClosingEntryAsync(
        Guid fiscalYearId, CancellationToken ct) =>
        await db.JournalEntries
            .AsNoTracking()
            .Where(e => e.ClosesFiscalYearId == fiscalYearId
                        && e.ReversesEntryId == null
                        && !db.JournalEntries.Any(r => r.ReversesEntryId == e.Id))
            .OrderByDescending(e => e.PostedAtUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Why the closing entry could not be posted into the period covering the year end.
    /// </summary>
    private async Task<List<string>> ClosingPeriodBlockersAsync(
        FiscalYear year, CancellationToken ct)
    {
        var period = await db.Periods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.LegalEntityId == year.LegalEntityId
                     && p.StartDate <= year.EndDate
                     && p.EndDate >= year.EndDate,
                ct);

        if (period is null)
        {
            return
            [
                $"No accounting period covers {year.EndDate:yyyy-MM-dd}, so the closing entry "
                + "has nowhere to be posted.",
            ];
        }

        if (period.State != PeriodState.Open)
        {
            return
            [
                $"Period {period.Sequence}, which covers {year.EndDate:yyyy-MM-dd}, is "
                + $"{period.State} and does not accept postings. The closing entry is dated "
                + "the last day of the year, so that period has to still be open when it is "
                + "posted — post the entry first, then close the month, then finalise.",
            ];
        }

        return [];
    }
}
