namespace Accounting.Api.Services;

// ---------------------------------------------------------------- fiscal years

/// <summary>A financial year and the periods to generate inside it.</summary>
/// <param name="PeriodCount">
/// Omit for calendar months, which is what almost every year wants. Give it to divide the
/// year into equal spans instead — thirteen for a 52/53-week year, or fewer for a short
/// first year that is not a whole number of months.
/// </param>
public record CreateFiscalYearRequest(
    Guid LegalEntityId,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate,
    int? PeriodCount = null);

/// <summary>
/// A year, its state, and where its close has got to.
/// </summary>
/// <param name="ClosingEntryNo">
/// The entry that transferred the year's result to retained earnings, if one has been
/// posted. Null means the year has not been closed off yet, and finalising is refused.
/// </param>
public record FiscalYearSummary(
    Guid Id,
    Guid LegalEntityId,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate,
    string State,
    int PeriodCount,
    int OpenPeriodCount,
    Guid? ClosingEntryId,
    string? ClosingEntryNo,
    bool ClosingEntryIsReversed)
{
    /// <summary>A year is ready to finalise once an un-reversed closing entry stands.</summary>
    public bool CanFinalise =>
        State != nameof(Models.PeriodState.HardClosed)
        && ClosingEntryId is not null
        && !ClosingEntryIsReversed;
}

public record FinaliseFiscalYearRequest(string Reason);

// ---------------------------------------------------------------- periods

public record PeriodSummary(
    Guid Id,
    Guid FiscalYearId,
    string FiscalYearCode,
    int Sequence,
    DateOnly StartDate,
    DateOnly EndDate,
    string State,
    int EntryCount);

/// <summary>A recorded state transition. Never updated, never deleted.</summary>
public record PeriodEventSummary(
    Guid Id,
    Guid PeriodId,
    int PeriodSequence,
    string FromState,
    string ToState,
    DateTimeOffset AtUtc,
    string ByUser,
    string Reason);

/// <summary>
/// A close or a reopen. The reason is mandatory in both directions.
/// </summary>
/// <remarks>
/// Requiring it on a close as well as a reopen is deliberate. "Month-end" is a perfectly
/// good reason, and asking for one every time means the field is never the tell-tale that
/// somebody was doing something unusual.
/// </remarks>
public record ChangePeriodStateRequest(string Reason);

/// <summary>
/// What stands between a period and being closed.
/// </summary>
/// <param name="Blockers">
/// Reasons the close will be refused. Empty when it will succeed.
/// </param>
/// <param name="Drafts">
/// Draft documents dated inside the period. These do not block the close — a draft is not in
/// the books and may simply have been abandoned — but once the period is closed there is no
/// posting them, so they are worth seeing first.
/// </param>
public record PeriodReadiness(
    Guid PeriodId,
    int Sequence,
    DateOnly StartDate,
    DateOnly EndDate,
    string State,
    int PostedEntryCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<DraftDocumentCount> Drafts)
{
    public bool CanSoftClose => Blockers.Count == 0;

    public int DraftCount => Drafts.Sum(d => d.Count);
}

public record DraftDocumentCount(string DocumentType, int Count);

// ---------------------------------------------------------------- year-end close

/// <summary>
/// What the closing entry would post, computed the same way the real one is.
/// </summary>
/// <remarks>
/// Offered because the close is two steps on purpose: posting the entry is reversible,
/// finalising the year is not, and an accountant should be able to read the figures before
/// starting either.
/// </remarks>
public record ClosingEntryPreview(
    Guid FiscalYearId,
    string FiscalYearCode,
    DateOnly EntryDate,
    string CurrencyCode,
    IReadOnlyList<ClosingEntryLine> Lines,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetResult,
    string RetainedEarningsAccountCode,
    IReadOnlyList<string> Blockers)
{
    public bool CanPost => Blockers.Count == 0 && Lines.Count >= 2;
}

public record ClosingEntryLine(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string Direction,
    decimal Amount);

public record PostClosingEntryRequest(string? Memo = null);
