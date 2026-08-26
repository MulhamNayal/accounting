namespace Accounting.Api.Services;

public record RunConsolidationRequest(
    DateOnly AsOf,
    string PresentationCurrency,
    string? Note = null);

public record ConsolidationRunSummary(
    Guid Id,
    DateOnly AsOf,
    string PresentationCurrency,
    DateTimeOffset CreatedAtUtc,
    string? Note,
    int LineCount);

/// <summary>
/// A consolidation, with each figure broken down by where it came from.
/// </summary>
/// <remarks>
/// <see cref="TotalDebit"/> and <see cref="TotalCredit"/> must be equal. The translation
/// reserve exists precisely to make that true when entities are translated at differing
/// rates, so an imbalance here means something is wrong rather than merely unrounded.
/// </remarks>
public record ConsolidationResult(
    Guid Id,
    DateOnly AsOf,
    string PresentationCurrency,
    DateTimeOffset CreatedAtUtc,
    string? Note,
    IReadOnlyList<ConsolidatedLine> Lines,
    IReadOnlyList<EntityContribution> Contributions,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>
/// One account's group figure, and the three things that produced it.
/// </summary>
/// <param name="EntityTotal">The entities' own balances, translated.</param>
/// <param name="Eliminations">Removal of transactions within the group.</param>
/// <param name="Translation">Residue from translating at differing rates.</param>
/// <param name="Consolidated">The sum of the three â€” what the group reports.</param>
public record ConsolidatedLine(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal EntityTotal,
    decimal Eliminations,
    decimal Translation,
    decimal Consolidated);

public record EntityContribution(string EntityCode, decimal Total);

/// <summary>
/// Intercompany balances between a pair of entities.
/// </summary>
/// <remarks>
/// <see cref="NetBalance"/> should be nothing. A figure here means one entity has recorded
/// something its counterpart has not, and consolidating would bury that in the group numbers.
/// </remarks>
public record IntercompanyPair(
    string FromEntity,
    string ToEntity,
    decimal NetBalance,
    int PostingCount);

public record UpsertExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    DateOnly RateDate,
    decimal ClosingRate,
    decimal? AverageRate = null,
    string? Source = null);

public record ExchangeRateSummary(
    Guid Id,
    string FromCurrency,
    string ToCurrency,
    DateOnly RateDate,
    decimal ClosingRate,
    decimal? AverageRate,
    string? Source);
