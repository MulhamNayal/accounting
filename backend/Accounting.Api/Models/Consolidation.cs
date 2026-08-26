namespace Accounting.Api.Models;

/// <summary>
/// A rate between two currencies on a date.
/// </summary>
/// <remarks>
/// Postings store the rate they used, so this table is not needed to value a transaction
/// after the fact. It exists for the operations that need a rate the documents cannot supply:
/// translating an entity into a group's presentation currency, and revaluing open balances at
/// period end.
/// <para>
/// <see cref="AverageRate"/> is held alongside the closing rate because translation needs
/// both — the balance sheet at the rate on the day, the income statement at the average over
/// the period, since income arose throughout it rather than at the end.
/// </para>
/// </remarks>
public class ExchangeRate
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>ISO 4217 of the currency being converted from.</summary>
    public required string FromCurrency { get; set; }

    /// <summary>ISO 4217 of the currency being converted to.</summary>
    public required string ToCurrency { get; set; }

    public DateOnly RateDate { get; set; }

    /// <summary>The rate on <see cref="RateDate"/>. One unit of From buys this much To.</summary>
    public decimal ClosingRate { get; set; }

    /// <summary>The average over the period ending on <see cref="RateDate"/>, when known.</summary>
    public decimal? AverageRate { get; set; }

    /// <summary>Where the figure came from, so a restatement can be traced.</summary>
    public string? Source { get; set; }
}

/// <summary>
/// One consolidation, kept rather than recomputed.
/// </summary>
/// <remarks>
/// Stored because a published consolidated statement must be reproducible. Recomputing it
/// later would silently pick up rates and eliminations as they stand then, so the figures
/// somebody signed would no longer be the figures the system reports.
/// </remarks>
public class ConsolidationRun
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public DateOnly AsOf { get; set; }

    /// <summary>The currency the group reports in.</summary>
    public required string PresentationCurrency { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string? Note { get; set; }

    public ICollection<ConsolidationPosting> Postings { get; set; } = [];
}

/// <summary>
/// One line of a consolidation, traceable to where it came from.
/// </summary>
/// <remarks>
/// Held separately from <c>postings</c> and never mixed with them. These are group figures:
/// they are not any entity's books and must never appear in a statutory filing for a single
/// entity.
/// </remarks>
public class ConsolidationPosting
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ConsolidationRunId { get; set; }
    public ConsolidationRun? ConsolidationRun { get; set; }

    /// <summary>Null on a group-level line that belongs to no single entity.</summary>
    public Guid? LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public PostingDirection Direction { get; set; }

    /// <summary>The amount in the entity's own functional currency.</summary>
    public decimal FunctionalAmount { get; set; }

    /// <summary>The same amount translated into the group's presentation currency.</summary>
    public decimal PresentationAmount { get; set; }

    /// <summary>Whether this is an entity balance, an elimination, or translation residue.</summary>
    public ConsolidationLineKind Kind { get; set; }

    /// <summary>The rate used, so any figure can be traced back to how it was arrived at.</summary>
    public decimal RateUsed { get; set; }
}
