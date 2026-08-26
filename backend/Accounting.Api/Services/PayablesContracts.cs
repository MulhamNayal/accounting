namespace Accounting.Api.Services;

// ---------------------------------------------------------------- suppliers

public record SupplierSummary(
    Guid Id,
    string Code,
    string Name,
    string CurrencyCode,
    int CreditTermDays,
    bool IsActive);

// ---------------------------------------------------------------- purchase invoices

public record CreatePurchaseInvoiceRequest(
    Guid LegalEntityId,
    Guid SupplierId,
    string SupplierInvoiceNo,
    DateOnly DocDate,
    IReadOnlyList<CreatePurchaseInvoiceLine> Lines,
    DateOnly? DueDate = null,
    string? CurrencyCode = null,
    decimal? FxRate = null,
    string? Memo = null);

public record CreatePurchaseInvoiceLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid ChargeAccountId,
    Guid? TaxCodeId = null,
    Guid? ProjectId = null);

public record PurchaseInvoiceLineDetail(
    Guid Id,
    int LineNo,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid ChargeAccountId,
    string ChargeAccountCode,
    string ChargeAccountName,
    Guid? TaxCodeId,
    string? TaxCodeLabel,
    decimal TaxRate,
    decimal TaxAmount,
    bool TaxReclaimable,
    /// <summary>
    /// What the charge account actually bore: the net, plus tax that could not be reclaimed.
    /// Shown because it is the figure that reaches the profit and loss account, and it differs
    /// from the net whenever the regime does not allow a reclaim.
    /// </summary>
    decimal ChargeAmount);

public record PurchaseInvoiceDetail(
    Guid Id,
    string? DocNo,
    string SupplierInvoiceNo,
    DateOnly DocDate,
    DateOnly DueDate,
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    string CurrencyCode,
    decimal FxRate,
    string? Memo,
    string State,
    Guid? JournalEntryId,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    IReadOnlyList<PurchaseInvoiceLineDetail> Lines);

public record PurchaseInvoiceSummary(
    Guid Id,
    string? DocNo,
    string SupplierInvoiceNo,
    DateOnly DocDate,
    DateOnly DueDate,
    string SupplierName,
    string CurrencyCode,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    string State,
    Guid? JournalEntryId);

// ---------------------------------------------------------------- payments

public record CreatePaymentRequest(
    Guid LegalEntityId,
    Guid SupplierId,
    Guid BankAccountId,
    DateOnly PaymentDate,
    decimal Amount,
    string? CurrencyCode = null,
    decimal? FxRate = null,
    string? Reference = null,
    string? Memo = null);

public record PaymentSummary(
    Guid Id,
    string? DocNo,
    DateOnly PaymentDate,
    string SupplierName,
    string CurrencyCode,
    decimal Amount,
    decimal Allocated,
    decimal Unallocated,
    string State,
    Guid? JournalEntryId);

// ---------------------------------------------------------------- allocation

public record AllocatePaymentRequest(
    Guid PaymentId,
    IReadOnlyList<AllocatePaymentLine> Lines);

public record AllocatePaymentLine(Guid PurchaseInvoiceId, decimal Amount);

public record PaymentAllocationDetail(
    Guid Id,
    Guid SupplierPaymentId,
    string? PaymentDocNo,
    Guid PurchaseInvoiceId,
    string? InvoiceDocNo,
    decimal Amount,
    decimal FunctionalAmount,
    decimal FxGainLossFunctional,
    Guid? JournalEntryId,
    DateTimeOffset AllocatedAtUtc,
    Guid? ReversesAllocationId);

// ---------------------------------------------------------------- reporting

public record OpenPurchaseInvoice(
    Guid Id,
    string? DocNo,
    string SupplierInvoiceNo,
    DateOnly DocDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal Gross,
    decimal Allocated,
    decimal Outstanding,
    int DaysOverdue);

public record SupplierBalance(
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    decimal Balance,
    decimal NotYetDue,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90);

public record PayablesAgeingReport(
    DateOnly AsOf,
    IReadOnlyList<SupplierBalance> Rows,
    decimal TotalOutstanding);

public record SupplierStatementLine(
    DateOnly Date,
    string SourceDocumentType,
    string EntryNo,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public record SupplierStatement(
    Guid SupplierId,
    string SupplierName,
    DateOnly AsOf,
    IReadOnlyList<SupplierStatementLine> Lines,
    decimal ClosingBalance);
