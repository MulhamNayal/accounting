namespace Accounting.Api.Services;

public record CreateSalesInvoiceRequest(
    Guid LegalEntityId,
    Guid CustomerId,
    DateOnly DocDate,
    IReadOnlyList<CreateSalesInvoiceLineRequest> Lines,
    DateOnly? DueDate = null,
    string? CurrencyCode = null,
    decimal? FxRate = null,
    string? Reference = null,
    string? Memo = null);

public record CreateSalesInvoiceLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid RevenueAccountId,
    Guid? ProjectId = null,
    Guid? AgentId = null,
    /// <summary>Null means outside the tax regime â€” not the same as zero-rated.</summary>
    Guid? TaxCodeId = null);

public record SalesInvoiceSummary(
    Guid Id,
    string? DocNo,
    DateOnly DocDate,
    DateOnly DueDate,
    string CustomerName,
    string CurrencyCode,
    /// <summary>Net of tax.</summary>
    decimal Total,
    decimal TaxTotal,
    /// <summary>What the customer owes. This is the figure that matters for settlement.</summary>
    decimal TotalWithTax,
    string State,
    Guid? JournalEntryId);

public record SalesInvoiceDetail(
    Guid Id,
    string? DocNo,
    DateOnly DocDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string CurrencyCode,
    decimal FxRate,
    string? Reference,
    string? Memo,
    string State,
    Guid? JournalEntryId,
    decimal Total,
    decimal TaxTotal,
    decimal TotalWithTax,
    IReadOnlyList<SalesInvoiceLineDetail> Lines);

public record SalesInvoiceLineDetail(
    Guid Id,
    int LineNo,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid RevenueAccountId,
    string RevenueAccountCode,
    string RevenueAccountName,
    Guid? TaxCodeId,
    string? TaxCodeName,
    decimal TaxRate,
    decimal TaxAmount);

/// <summary>Returned by <see cref="ICustomerService"/>. No balance â€” that comes from postings.</summary>
public record CustomerSummary(
    Guid Id,
    string Code,
    string Name,
    string? TaxId,
    string CurrencyCode,
    int CreditTermDays,
    bool IsActive);
