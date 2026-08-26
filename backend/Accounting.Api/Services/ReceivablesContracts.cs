namespace Accounting.Api.Services;

public record CreateReceiptRequest(
    Guid LegalEntityId,
    Guid CustomerId,
    Guid BankAccountId,
    DateOnly ReceiptDate,
    decimal Amount,
    string? CurrencyCode = null,
    decimal? FxRate = null,
    string? Reference = null,
    string? Memo = null);

public record ReceiptSummary(
    Guid Id,
    string? DocNo,
    DateOnly ReceiptDate,
    string CustomerName,
    string CurrencyCode,
    decimal Amount,
    decimal Allocated,
    decimal Unallocated,
    string State,
    Guid? JournalEntryId);

public record AllocateRequest(Guid ReceiptId, IReadOnlyList<AllocationLineRequest> Lines);

public record AllocationLineRequest(Guid SalesInvoiceId, decimal Amount);

public record AllocationDetail(
    Guid Id,
    Guid CustomerReceiptId,
    string? ReceiptDocNo,
    Guid SalesInvoiceId,
    string? InvoiceDocNo,
    decimal Amount,
    decimal FunctionalAmount,
    decimal FxGainLossFunctional,
    /// <summary>The exchange-difference entry, if the two rates differed. Null otherwise.</summary>
    Guid? JournalEntryId,
    DateTimeOffset AllocatedAtUtc,
    Guid? ReversesAllocationId);

/// <summary>
/// What a customer owes, derived from postings and allocations rather than stored.
/// </summary>
public record CustomerBalance(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal Balance,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90);

/// <summary>
/// An ageing report. <see cref="Total"/> must equal the receivables control account
/// balance in the trial balance — they are computed from the same postings, so a
/// difference would mean a defect rather than a reconciliation task.
/// </summary>
public record AgeingReport(
    DateOnly AsOf,
    IReadOnlyList<CustomerBalance> Customers,
    decimal Total);

public record OpenInvoice(
    Guid Id,
    string? DocNo,
    DateOnly DocDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal Total,
    decimal Allocated,
    decimal Outstanding,
    int DaysOverdue);

/// <summary>A customer's account: every document, oldest first, with a running balance.</summary>
public record CustomerStatement(
    Guid CustomerId,
    string CustomerName,
    DateOnly AsOf,
    IReadOnlyList<StatementLine> Lines,
    decimal ClosingBalance);

public record StatementLine(
    DateOnly Date,
    string DocumentType,
    string? DocNo,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);
