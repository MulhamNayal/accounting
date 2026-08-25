namespace ClearWise.Api.Models;

/// <summary>When a series restarts its count.</summary>
public enum NumberResetPolicy
{
    /// <summary>One continuous run for the life of the entity.</summary>
    Never = 1,

    /// <summary>Restarts each financial year — the usual expectation for tax documents.</summary>
    Yearly = 2,
}

/// <summary>
/// A numbering scheme for one document type in one entity.
/// </summary>
/// <remarks>
/// Several series may be active for the same document type — a business commonly runs a
/// main invoice series alongside one for a branch or a project. The incumbent system's
/// production data carried two concurrent invoice series with entirely different formats,
/// so this is a requirement rather than flexibility for its own sake.
/// </remarks>
public class NumberSeries
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Which documents draw from this series, e.g. "JournalEntry", "SalesInvoice".</summary>
    public required string DocumentType { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// A .NET composite format string. <c>{0}</c> is the number, <c>{1}</c> the document
    /// date — so "IV-{0:D5}" gives IV-00001 and "IV/{1:yyyy}/{0:D4}" gives IV/2026/0001.
    /// </summary>
    public required string Format { get; set; }

    public NumberResetPolicy ResetPolicy { get; set; } = NumberResetPolicy.Yearly;

    /// <summary>
    /// Whether the sequence must be dense — no missing numbers, ever.
    /// </summary>
    /// <remarks>
    /// True for documents a tax authority examines: sales invoices, credit notes, debit
    /// notes. A gap invites the question "where did that invoice go", so a cancelled
    /// document must still occupy its number rather than vanish.
    /// <para>
    /// Density costs concurrency. The counter row is locked until the document's
    /// transaction commits, so inserts for the same series serialise. That price is worth
    /// paying only where the law requires it, which is why it is a per-series flag rather
    /// than a global rule.
    /// </para>
    /// </remarks>
    public bool IsGapless { get; set; }

    /// <summary>The series used when a caller does not name one.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<NumberCounter> Counters { get; set; } = [];
}

/// <summary>
/// The running count for one series in one reset window.
/// </summary>
/// <remarks>
/// Separate from <see cref="NumberSeries"/> because a yearly series needs one count per
/// year, and because this row is locked on every allocation — keeping it narrow keeps the
/// lock cheap and avoids blocking readers of the series definition.
/// </remarks>
public class NumberCounter
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid NumberSeriesId { get; set; }
    public NumberSeries? NumberSeries { get; set; }

    /// <summary>The reset window: the year for a yearly series, empty for a continuous one.</summary>
    public required string PeriodKey { get; set; }

    public long NextNumber { get; set; } = 1;
}
