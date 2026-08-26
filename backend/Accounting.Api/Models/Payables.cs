namespace Accounting.Api.Models;

/// <summary>
/// Someone the business owes money to.
/// </summary>
/// <remarks>
/// Held at tenant level for the same reason <see cref="Customer"/> is: a group buying from
/// the same supplier through two companies needs one record for that supplier, or the two
/// sides can never be matched and every detail change has to be made twice.
/// <para>
/// There is no balance field. What is owed to a supplier is the sum of postings to a payables
/// control account carrying their id — the same rows the control account is computed from, so
/// the two cannot disagree.
/// </para>
/// </remarks>
public class Supplier
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? RegistrationNo { get; set; }

    public string? TaxId { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    /// <summary>The currency this supplier normally invoices in.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>Days from invoice date to when payment is due.</summary>
    public int CreditTermDays { get; set; } = 30;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// A bill from a supplier.
/// </summary>
/// <remarks>
/// The mirror of <see cref="SalesInvoice"/>, and holds no balances for the same reason: what
/// remains payable is derived from the invoice's payable posting less the allocations against
/// it.
/// <para>
/// One thing genuinely differs from the sales side. A sales invoice charges tax the business
/// collects and owes onward; a purchase invoice pays tax the business may or may not be able
/// to reclaim. Where the regime allows a reclaim the tax is an asset; where it does not, the
/// tax is part of what the thing cost and belongs in the expense. The posting rule decides
/// that per line from the tax code, which is why <see cref="PurchaseInvoiceLine.TaxRate"/>
/// alone is not enough to reproduce the entry.
/// </para>
/// </remarks>
public class PurchaseInvoice
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Our own internal number. Null while a draft.</summary>
    public string? DocNo { get; set; }

    /// <summary>
    /// The number printed on the supplier's invoice.
    /// </summary>
    /// <remarks>
    /// Required, and unique per supplier — entering the same bill twice is the single most
    /// common payables error, and a duplicate is far cheaper to refuse than to unpick after
    /// it has been paid. There is no equivalent control on the sales side because there the
    /// business issues the numbers itself.
    /// </remarks>
    public required string SupplierInvoiceNo { get; set; }

    public DateOnly DocDate { get; set; }

    public DateOnly DueDate { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public required string CurrencyCode { get; set; }

    public decimal FxRate { get; set; } = 1m;

    public string? Memo { get; set; }

    public DocumentState State { get; set; } = DocumentState.Draft;

    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<PurchaseInvoiceLine> Lines { get; set; } = [];

    /// <summary>Net of tax. Derived, never stored.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);

    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);

    /// <summary>What the supplier is owed.</summary>
    public decimal TotalWithTax => Total + TaxTotal;
}

public class PurchaseInvoiceLine
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public int LineNo { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>
    /// What this line is charged to.
    /// </summary>
    /// <remarks>
    /// Named for the charge rather than for expense because it is not always an expense: a
    /// capital purchase debits a fixed asset and stock bought for resale debits inventory.
    /// Constraining this to expense accounts would make the common case read nicely and the
    /// other two impossible.
    /// </remarks>
    public Guid ChargeAccountId { get; set; }
    public Account? ChargeAccount { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>Null means outside the tax regime, which is not the same as zero-rated.</summary>
    public Guid? TaxCodeId { get; set; }
    public TaxCode? TaxCode { get; set; }

    /// <summary>The rate applied, copied at draft time so the line always reproduces its tax.</summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Whether the tax on this line could be reclaimed, copied from the regime at draft time.
    /// </summary>
    /// <remarks>
    /// Stored rather than looked up because it changes where the tax was posted, and a regime
    /// can be superseded. Malaysia's GST was reclaimable and the SST that replaced it is not,
    /// so a bill dated 2017 and one dated today post differently and must both stay
    /// reproducible.
    /// </remarks>
    public bool TaxReclaimable { get; set; }

    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 4, MidpointRounding.ToEven);

    /// <summary>Tax on this line, rounded per line to match the sales side's convention.</summary>
    public decimal TaxAmount =>
        decimal.Round(LineTotal * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal LineTotalWithTax => LineTotal + TaxAmount;

    /// <summary>
    /// What the charge account actually bears: the net, plus any tax that cannot be reclaimed.
    /// </summary>
    public decimal ChargeAmount => TaxReclaimable ? LineTotal : LineTotal + TaxAmount;
}

/// <summary>
/// Money paid to a supplier.
/// </summary>
/// <remarks>
/// Separate from deciding which bills it settles, exactly as a receipt is. A payment run
/// often covers several invoices with one transfer.
/// </remarks>
public class SupplierPayment
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    public string? DocNo { get; set; }

    public DateOnly PaymentDate { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Which bank or cash account the money left from.</summary>
    public Guid BankAccountId { get; set; }
    public Account? BankAccount { get; set; }

    public required string CurrencyCode { get; set; }

    public decimal FxRate { get; set; } = 1m;

    public decimal Amount { get; set; }

    /// <summary>Cheque number, transfer reference, whatever the bank shows.</summary>
    public string? Reference { get; set; }

    public string? Memo { get; set; }

    public DocumentState State { get; set; } = DocumentState.Draft;

    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }
}

/// <summary>
/// A decision that a payment settles part or all of a purchase invoice.
/// </summary>
/// <remarks>
/// Append-only, like <see cref="Allocation"/>. A separate table rather than a generalised one
/// shared with receivables: the two carry different foreign keys, and widening
/// <see cref="Allocation"/> to nullable columns for both would trade a clear constraint for a
/// pair of check constraints that say the same thing less well.
/// </remarks>
public class PaymentAllocation
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }

    public Guid SupplierPaymentId { get; set; }
    public SupplierPayment? SupplierPayment { get; set; }

    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public decimal Amount { get; set; }

    public decimal FunctionalAmount { get; set; }

    /// <summary>
    /// The exchange difference realised by this settlement: the allocated amount at the
    /// invoice's rate less the same amount at the payment's rate. Positive means more was
    /// owed than was actually paid — a gain, which is the opposite sign to the receivables
    /// case because a payable is a credit balance.
    /// </summary>
    public decimal FxGainLossFunctional { get; set; }

    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset AllocatedAtUtc { get; set; }

    public Guid AllocatedByUserId { get; set; }

    public Guid? ReversesAllocationId { get; set; }
    public PaymentAllocation? ReversesAllocation { get; set; }
}
