namespace ClearWise.Api.Models;

/// <summary>
/// One debit or credit line. Append-only: the application role holds INSERT and SELECT on
/// this table and nothing else.
/// </summary>
/// <remarks>
/// This is the single place a financial fact is recorded. There is no separate sales or
/// purchase ledger holding the same figure again, so a customer balance and its control
/// account cannot disagree — they are the same rows, filtered differently. The dimensions
/// below are what make that filtering possible.
/// </remarks>
public class Posting
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }

    public Guid JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    /// <summary>Ordinal within the entry, starting at 1. Presentation only.</summary>
    public int LineNo { get; set; }

    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public PostingDirection Direction { get; set; }

    /// <summary>Amount in <see cref="CurrencyCode"/>. Always positive; the side is
    /// <see cref="Direction"/>, not the sign.</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code the transaction was denominated in.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>
    /// The same value in the entity's functional currency. Entries balance in this
    /// currency, not the transaction one — different units cannot be summed.
    /// </summary>
    public decimal FunctionalAmount { get; set; }

    /// <summary>
    /// The rate actually used, stored rather than looked up later. A historical posting must
    /// always reproduce the same functional figure, and rate tables get corrected.
    /// </summary>
    public decimal FxRate { get; set; }

    // ---- Dimensions -------------------------------------------------------------
    // Reporting axes carried by the posting itself, so customer ageing, project results
    // and agent commission all derive from the ledger rather than from parallel tables
    // that can drift away from it.
    //
    // These are plain nullable columns for now: the customer, supplier, item and tax
    // tables arrive in Layers 3-5, and foreign keys are added alongside them.

    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? TaxCodeId { get; set; }

    /// <summary>
    /// Set when this posting arises from a transaction with a sister entity in the same
    /// tenant, so consolidation can pair the two sides and eliminate them.
    /// </summary>
    public Guid? IntercompanyEntityId { get; set; }
    public LegalEntity? IntercompanyEntity { get; set; }

    public string? Description { get; set; }
}
