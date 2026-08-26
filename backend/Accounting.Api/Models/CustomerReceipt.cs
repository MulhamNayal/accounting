namespace Accounting.Api.Models;

/// <summary>
/// Money received from a customer.
/// </summary>
/// <remarks>
/// Deliberately separate from the act of deciding which invoices it settles. A customer
/// often pays a round figure against several invoices, or on account before any invoice
/// exists, so receiving money and allocating it are two decisions and are recorded as two
/// things. See <see cref="Allocation"/>.
/// </remarks>
public class CustomerReceipt
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Null while a draft. Assigned from a series at posting.</summary>
    public string? DocNo { get; set; }

    public DateOnly ReceiptDate { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Which bank or cash account the money landed in.</summary>
    public Guid BankAccountId { get; set; }
    public Account? BankAccount { get; set; }

    public required string CurrencyCode { get; set; }

    /// <summary>Rate to the entity's functional currency on the day it was received.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>Amount received, in <see cref="CurrencyCode"/>.</summary>
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
/// A decision that a receipt settles part or all of an invoice.
/// </summary>
/// <remarks>
/// Append-only. Un-allocating inserts a reversing row rather than deleting, because how
/// money was applied is itself a fact worth keeping — a customer disputing which invoice
/// their payment cleared is a real conversation, and "we changed our minds and there is no
/// record" is not an answer.
/// <para>
/// This is the one thing in the receivables subledger that genuinely cannot be derived.
/// Balances and ageing fall out of the postings; which invoice a payment was applied to is
/// a choice somebody made.
/// </para>
/// </remarks>
public class Allocation
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }

    /// <summary>The money being applied.</summary>
    public Guid CustomerReceiptId { get; set; }
    public CustomerReceipt? CustomerReceipt { get; set; }

    /// <summary>The invoice being settled.</summary>
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    /// <summary>Amount applied, in the transaction currency shared by both documents.</summary>
    public decimal Amount { get; set; }

    /// <summary>The same amount at the receipt's rate.</summary>
    public decimal FunctionalAmount { get; set; }

    /// <summary>
    /// The exchange difference realised by this settlement: the allocated amount at the
    /// invoice's rate less the same amount at the receipt's rate. Positive means the
    /// receivable was carried at more than was actually realised — a loss.
    /// </summary>
    public decimal FxGainLossFunctional { get; set; }

    /// <summary>The entry that posted the exchange difference. Null when there was none.</summary>
    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset AllocatedAtUtc { get; set; }

    public Guid AllocatedByUserId { get; set; }

    /// <summary>Set when this row undoes an earlier allocation. Points backwards, as ever.</summary>
    public Guid? ReversesAllocationId { get; set; }
    public Allocation? ReversesAllocation { get; set; }
}
