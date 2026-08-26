namespace Accounting.Api.Services;

// ---------------------------------------------------------------- sales credit notes

public record CreateSalesCreditNoteRequest(
    Guid LegalEntityId,
    Guid SalesInvoiceId,
    DateOnly DocDate,
    string ReasonCode,
    IReadOnlyList<CreateCreditNoteLine> Lines,
    string? Memo = null);

/// <summary>
/// One line of a credit note. The account is the one being credited back — the revenue
/// account on a sales credit, the charge account on a purchase credit.
/// </summary>
public record CreateCreditNoteLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid AccountId,
    Guid? TaxCodeId = null,
    Guid? ProjectId = null);

public record CreditNoteLineDetail(
    Guid Id,
    int LineNo,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    Guid? TaxCodeId,
    string? TaxCodeLabel,
    decimal TaxRate,
    decimal TaxAmount);

public record SalesCreditNoteDetail(
    Guid Id,
    string? DocNo,
    DateOnly DocDate,
    Guid SalesInvoiceId,
    string? InvoiceDocNo,
    Guid CustomerId,
    string CustomerName,
    string CurrencyCode,
    decimal FxRate,
    string ReasonCode,
    string? Memo,
    string State,
    Guid? JournalEntryId,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    IReadOnlyList<CreditNoteLineDetail> Lines);

public record SalesCreditNoteSummary(
    Guid Id,
    string? DocNo,
    DateOnly DocDate,
    string? InvoiceDocNo,
    string CustomerName,
    string CurrencyCode,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    string ReasonCode,
    string State,
    Guid? JournalEntryId);

// ---------------------------------------------------------------- purchase credit notes

public record CreatePurchaseCreditNoteRequest(
    Guid LegalEntityId,
    Guid PurchaseInvoiceId,
    DateOnly DocDate,
    string ReasonCode,
    IReadOnlyList<CreateCreditNoteLine> Lines,
    string? SupplierCreditNoteNo = null,
    string? Memo = null);

public record PurchaseCreditNoteDetail(
    Guid Id,
    string? DocNo,
    string? SupplierCreditNoteNo,
    DateOnly DocDate,
    Guid PurchaseInvoiceId,
    string SupplierInvoiceNo,
    Guid SupplierId,
    string SupplierName,
    string CurrencyCode,
    decimal FxRate,
    string ReasonCode,
    string? Memo,
    string State,
    Guid? JournalEntryId,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    IReadOnlyList<CreditNoteLineDetail> Lines);

public record PurchaseCreditNoteSummary(
    Guid Id,
    string? DocNo,
    string? SupplierCreditNoteNo,
    DateOnly DocDate,
    string SupplierInvoiceNo,
    string SupplierName,
    string CurrencyCode,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    string ReasonCode,
    string State,
    Guid? JournalEntryId);
