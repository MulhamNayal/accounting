namespace Accounting.Api.Services;

/// <summary>A request to post one balanced journal entry.</summary>
public record PostJournalEntryRequest(
    Guid LegalEntityId,
    DateOnly EntryDate,
    IReadOnlyList<PostingLineRequest> Lines,
    string? Memo = null,
    string? SourceDocumentType = null,
    Guid? SourceDocumentId = null);

/// <summary>A request to post the entry that closes a fiscal year.</summary>
/// <remarks>
/// Deliberately a separate shape from <see cref="PostJournalEntryRequest"/>, which is bound
/// from a request body. The profit and loss account excludes closing entries, so a client
/// able to mark any entry as one would have a way to hide a year's income from the report.
/// Nothing reachable from a controller can set this.
/// </remarks>
public record PostClosingJournalEntryRequest(
    Guid LegalEntityId,
    Guid FiscalYearId,
    DateOnly EntryDate,
    IReadOnlyList<PostingLineRequest> Lines,
    string? Memo = null);

/// <summary>
/// One line. <see cref="Amount"/> is always positive — the side is
/// <see cref="Direction"/>, never the sign.
/// </summary>
public record PostingLineRequest(
    Guid AccountId,
    string Direction,
    decimal Amount,
    string? CurrencyCode = null,
    decimal? FxRate = null,
    Guid? CustomerId = null,
    Guid? SupplierId = null,
    Guid? ItemId = null,
    Guid? ProjectId = null,
    Guid? AgentId = null,
    Guid? AreaId = null,
    /// <summary>
    /// Carried onto the posting so a tax return can be filed from the ledger rather than
    /// from the documents, and so a superseded regime's history stays intact.
    /// </summary>
    Guid? TaxCodeId = null,
    /// <summary>
    /// Names a sister entity when this posting arises from a transaction within the group,
    /// so consolidation can eliminate it.
    /// </summary>
    /// <remarks>
    /// Must be set here, at posting time. Postings are immutable, so there is no marking a
    /// transaction as intercompany afterwards — and that is the right constraint: it is a
    /// deliberate statement about what happened, not something to be inferred later by
    /// matching amounts, which would eliminate genuine third-party trade.
    /// </remarks>
    Guid? IntercompanyEntityId = null,
    string? Description = null);

public record ReverseEntryRequest(string ReasonCode);

public record JournalEntrySummary(
    Guid Id,
    string EntryNo,
    DateOnly EntryDate,
    string SourceDocumentType,
    string? Memo,
    decimal TotalDebit,
    int LineCount,
    bool IsReversal,
    bool IsReversed);

public record JournalEntryDetail(
    Guid Id,
    string EntryNo,
    DateOnly EntryDate,
    string SourceDocumentType,
    string? Memo,
    DateTimeOffset PostedAtUtc,
    Guid? ReversesEntryId,
    Guid? SupersedesEntryId,
    Guid? ReversedByEntryId,
    string? ReasonCode,
    IReadOnlyList<PostingLine> Lines);

public record PostingLine(
    Guid Id,
    int LineNo,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string Direction,
    decimal Amount,
    string CurrencyCode,
    decimal FunctionalAmount,
    decimal FxRate,
    Guid? CustomerId,
    string? Description);

public record TrialBalanceLine(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    decimal Balance);

/// <summary>
/// <see cref="TotalDebit"/> and <see cref="TotalCredit"/> must be equal. If they are not,
/// something has gone wrong that the database was supposed to make impossible.
/// </summary>
public record TrialBalance(
    DateOnly AsOf,
    IReadOnlyList<TrialBalanceLine> Lines,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}
