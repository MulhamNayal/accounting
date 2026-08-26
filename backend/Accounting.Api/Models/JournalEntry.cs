namespace Accounting.Api.Models;

/// <summary>
/// One balanced accounting event. Never updated and never deleted — the application role
/// does not hold those privileges on this table.
/// </summary>
/// <remarks>
/// There is no <c>UpdatedAt</c>, no <c>UpdateCount</c>, no <c>IsCancelled</c>. Their absence
/// is the design: a column recording mutation implies mutation is possible. Corrections are
/// new entries linked back to what they correct.
/// </remarks>
public class JournalEntry
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Allocated from a number series. Reversals consume a number of their own.</summary>
    public required string EntryNo { get; set; }

    /// <summary>
    /// The accounting date, which determines the period. Not the wall clock — back-dating
    /// into an open period is ordinary and permitted.
    /// </summary>
    public DateOnly EntryDate { get; set; }

    public Guid PeriodId { get; set; }
    public AccountingPeriod? Period { get; set; }

    /// <summary>What produced this entry: 'Manual', 'SalesInvoice', 'Payment', and so on.</summary>
    public required string SourceDocumentType { get; set; }

    public Guid? SourceDocumentId { get; set; }

    public DateTimeOffset PostedAtUtc { get; set; }

    public Guid PostedByUserId { get; set; }
    public AppUser? PostedBy { get; set; }

    /// <summary>Set when this entry reverses another. Points backwards, always.</summary>
    public Guid? ReversesEntryId { get; set; }
    public JournalEntry? Reverses { get; set; }

    /// <summary>
    /// Set when this entry is the replacement for one that was reversed.
    /// </summary>
    /// <remarks>
    /// Both links point from the new row to the old one. A forward pointer — say
    /// <c>ReplacedByEntryId</c> on the original — would require updating the original, which
    /// the revoked privileges forbid. Nothing about a posted entry ever changes, including
    /// its links.
    /// </remarks>
    public Guid? SupersedesEntryId { get; set; }
    public JournalEntry? Supersedes { get; set; }

    /// <summary>
    /// Mandatory on any entry that reverses another. A correction with no stated reason is
    /// precisely what an auditor asks about.
    /// </summary>
    public string? ReasonCode { get; set; }

    public string? Memo { get; set; }

    public ICollection<Posting> Postings { get; set; } = [];
}
