namespace ClearWise.Api.Services;

/// <summary>A request to post one balanced journal entry.</summary>
public record PostJournalEntryRequest(
    Guid LegalEntityId,
    DateOnly EntryDate,
    IReadOnlyList<PostingLineRequest> Lines,
    string? Memo = null,
    string? SourceDocumentType = null,
    Guid? SourceDocumentId = null);

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
