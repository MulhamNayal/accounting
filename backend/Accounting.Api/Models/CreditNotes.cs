namespace Accounting.Api.Models;

/// <summary>
/// A credit issued to a customer against an invoice.
/// </summary>
/// <remarks>
/// <para>
/// This is how a posted sales invoice is undone. The invoice itself is immutable — the
/// database refuses to change it — so reducing what a customer owes is a new document that
/// posts the opposite way, and the pair remains visible forever. That is the whole point:
/// "we reduced this by 200 on the 5th, for these goods, because they came back damaged" is a
/// better record than an invoice that quietly became smaller.
/// </para>
/// <para>
/// <b>The invoice is required.</b> A credit that names no invoice is a credit on account, and
/// supporting it would break an invariant worth more than the feature: ageing is computed by
/// walking invoices and subtracting what has been applied to them, and its total is provably
/// equal to the receivables control account. An unattached credit would reduce the control
/// account while leaving ageing untouched, and the two would silently diverge. Credits on
/// account can be added later by giving ageing a row for them; until then this constraint
/// keeps the guarantee honest.
/// </para>
/// </remarks>
public class SalesCreditNote
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Null while a draft. Assigned from a gapless series at posting.</summary>
    public string? DocNo { get; set; }

    public DateOnly DocDate { get; set; }

    /// <summary>The invoice being credited. Required — see the remarks on this class.</summary>
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public required string CurrencyCode { get; set; }

    /// <summary>
    /// Copied from the invoice rather than taken from today's rates.
    /// </summary>
    /// <remarks>
    /// A credit reverses part of what the invoice recorded, so it has to reverse it at the
    /// rate the invoice used. Crediting at a different rate would leave a residue on the
    /// receivable that no settlement ever clears, and it would look like an exchange
    /// difference nobody realised.
    /// </remarks>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>
    /// Why the credit was given.
    /// </summary>
    /// <remarks>
    /// Required. A reduction to a customer's debt that nobody can explain is the first thing
    /// an auditor asks about, and the second is who authorised it.
    /// </remarks>
    public required string ReasonCode { get; set; }

    public string? Memo { get; set; }

    public DocumentState State { get; set; } = DocumentState.Draft;

    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<SalesCreditNoteLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.LineTotal);

    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);

    /// <summary>What the customer's debt is reduced by.</summary>
    public decimal TotalWithTax => Total + TaxTotal;
}

public class SalesCreditNoteLine
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid SalesCreditNoteId { get; set; }
    public SalesCreditNote? SalesCreditNote { get; set; }

    public int LineNo { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>Which income account this line debits back.</summary>
    public Guid RevenueAccountId { get; set; }
    public Account? RevenueAccount { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? AgentId { get; set; }

    public Guid? TaxCodeId { get; set; }
    public TaxCode? TaxCode { get; set; }

    /// <summary>The rate the original invoice charged, so the credit reverses that exactly.</summary>
    public decimal TaxRate { get; set; }

    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 4, MidpointRounding.ToEven);

    /// <summary>Rounded per line, matching the invoice's convention so the pair reconciles.</summary>
    public decimal TaxAmount =>
        decimal.Round(LineTotal * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal LineTotalWithTax => LineTotal + TaxAmount;
}

/// <summary>
/// A credit received from a supplier against a bill.
/// </summary>
/// <remarks>
/// The mirror of <see cref="SalesCreditNote"/>, and required to name its bill for the same
/// reason. Often called a debit note when the business raises it rather than the supplier;
/// the accounting is identical either way, so there is one document and the supplier's own
/// reference records whose paperwork it is.
/// </remarks>
public class PurchaseCreditNote
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    public string? DocNo { get; set; }

    /// <summary>
    /// The supplier's reference for the credit, where they issued one.
    /// </summary>
    /// <remarks>
    /// Optional, unlike a bill's. A credit is often raised by us and sent to the supplier, in
    /// which case there is no document of theirs to record — and the duplicate control that
    /// makes a bill's reference mandatory does not apply, because a credit that arrives twice
    /// costs nothing.
    /// </remarks>
    public string? SupplierCreditNoteNo { get; set; }

    public DateOnly DocDate { get; set; }

    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public required string CurrencyCode { get; set; }

    /// <summary>Copied from the bill, for the reason given on the sales side.</summary>
    public decimal FxRate { get; set; } = 1m;

    public required string ReasonCode { get; set; }

    public string? Memo { get; set; }

    public DocumentState State { get; set; } = DocumentState.Draft;

    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<PurchaseCreditNoteLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.LineTotal);

    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);

    public decimal TotalWithTax => Total + TaxTotal;
}

public class PurchaseCreditNoteLine
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PurchaseCreditNoteId { get; set; }
    public PurchaseCreditNote? PurchaseCreditNote { get; set; }

    public int LineNo { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>The account the original bill charged, credited back.</summary>
    public Guid ChargeAccountId { get; set; }
    public Account? ChargeAccount { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? TaxCodeId { get; set; }
    public TaxCode? TaxCode { get; set; }

    public decimal TaxRate { get; set; }

    /// <summary>
    /// Whether the tax on the bill being credited was reclaimed.
    /// </summary>
    /// <remarks>
    /// Copied from the line being credited rather than resolved from the regime again. If the
    /// original tax went to input tax, the credit takes it back off input tax; if it went into
    /// the cost, the credit takes it back off the cost. Re-deriving it risks the two halves
    /// disagreeing when a regime has been superseded in between.
    /// </remarks>
    public bool TaxReclaimable { get; set; }

    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 4, MidpointRounding.ToEven);

    public decimal TaxAmount =>
        decimal.Round(LineTotal * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal LineTotalWithTax => LineTotal + TaxAmount;

    /// <summary>What comes back off the charge account: net, plus tax that was never reclaimable.</summary>
    public decimal ChargeAmount => TaxReclaimable ? LineTotal : LineTotal + TaxAmount;
}
