namespace ClearWise.Api.Models;

/// <summary>Where a document sits in its lifecycle.</summary>
public enum DocumentState
{
    /// <summary>Freely editable, has no postings, and is not part of the books.</summary>
    Draft = 1,

    /// <summary>Posted to the ledger. The one-way door — nothing about it changes again.</summary>
    Posted = 2,
}

/// <summary>
/// A sales invoice.
/// </summary>
/// <remarks>
/// <b>Holds no balances.</b> There is no "amount outstanding" column, because that is
/// derived from the invoice's receivable posting less the allocations against it. A stored
/// copy is the thing that eventually disagrees with the ledger, and chasing that difference
/// is the single most common support burden in accounting software.
/// <para>
/// A draft is ordinary mutable data. Posting allocates a gapless document number, runs the
/// posting rule, and writes an immutable journal entry — after which a database trigger
/// refuses any further change to this row.
/// </para>
/// </remarks>
public class SalesInvoice
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Null while a draft. Assigned from a gapless series at posting.</summary>
    public string? DocNo { get; set; }

    public DateOnly DocDate { get; set; }

    public DateOnly DueDate { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public required string CurrencyCode { get; set; }

    /// <summary>Rate to the entity's functional currency, fixed at posting.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The customer's own order or reference number.</summary>
    public string? Reference { get; set; }

    public string? Memo { get; set; }

    public DocumentState State { get; set; } = DocumentState.Draft;

    /// <summary>The entry this invoice produced. Null until posted.</summary>
    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<SalesInvoiceLine> Lines { get; set; } = [];

    /// <summary>Derived, never stored — a stored total is a total that can drift.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);
}

public class SalesInvoiceLine
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public int LineNo { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>Which income account this line credits.</summary>
    public Guid RevenueAccountId { get; set; }
    public Account? RevenueAccount { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? AgentId { get; set; }

    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 4, MidpointRounding.ToEven);
}
