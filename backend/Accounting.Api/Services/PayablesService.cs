using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IPayablesService
{
    Task<PaymentSummary> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default);

    Task<PaymentSummary> PostPaymentAsync(Guid paymentId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(Guid legalEntityId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentAllocationDetail>> AllocateAsync(
        AllocatePaymentRequest request, CancellationToken ct = default);

    Task<PaymentAllocationDetail> UnallocateAsync(Guid allocationId, CancellationToken ct = default);

    Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(
        Guid legalEntityId, Guid? supplierId, CancellationToken ct = default);

    Task<PayablesAgeingReport> GetAgeingAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default);

    Task<SupplierStatement> GetStatementAsync(
        Guid legalEntityId, Guid supplierId, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Payments, allocation, and everything the payables subledger reports.
/// </summary>
/// <remarks>
/// There is no AP ledger table, for the same reason there is no AR one. What is owed to a
/// supplier is the sum of postings to payables control accounts carrying their id — the rows
/// the control account itself is computed from. The subledger and the control account are the
/// same data, filtered differently, so they cannot drift apart.
/// </remarks>
public sealed class PayablesService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    ILogger<PayablesService> logger) : IPayablesService
{
    private const string DocumentType = "SupplierPayment";

    // ---------------------------------------------------------------- payments

    public async Task<PaymentSummary> CreatePaymentAsync(
        CreatePaymentRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var entity = await db.LegalEntities.FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
            ?? throw new NotFoundException($"No supplier with id {request.SupplierId}.");

        var bank = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.BankAccountId, ct)
            ?? throw new NotFoundException($"No account with id {request.BankAccountId}.");

        if (bank.ControlType != ControlType.Bank)
        {
            throw new PostingValidationException(
                $"Account {bank.Code} ({bank.Name}) is not a bank or cash account. "
                + "Money has to leave from somewhere that represents money.");
        }

        if (request.Amount <= 0)
        {
            throw new PostingValidationException(
                $"The payment is for {request.Amount}. A refund from a supplier is a receipt, "
                + "not a negative payment.");
        }

        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? supplier.CurrencyCode
            : request.CurrencyCode.ToUpperInvariant();

        var rate = request.FxRate ?? (currency == entity.FunctionalCurrency ? 1m : 0m);

        if (rate <= 0)
        {
            throw new PostingValidationException(
                $"The payment is in {currency} but the books are in {entity.FunctionalCurrency}, "
                + "and no exchange rate was given.");
        }

        var payment = new SupplierPayment
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            PaymentDate = request.PaymentDate,
            SupplierId = supplier.Id,
            BankAccountId = bank.Id,
            CurrencyCode = currency,
            FxRate = rate,
            Amount = request.Amount,
            Reference = request.Reference,
            Memo = request.Memo,
            State = DocumentState.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
        };

        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(ct);

        return await SummarisePaymentAsync(payment.Id, ct);
    }

    public async Task<PaymentSummary> PostPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var payment = await db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.BankAccount)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException($"No payment with id {paymentId}.");

        if (payment.State == DocumentState.Posted)
        {
            throw new PostingValidationException($"Payment {payment.DocNo} is already posted.");
        }

        var payables = await ResolvePayablesAccountAsync(payment.TenantId, ct);

        // Debit payables, credit the bank — the opposite of a receipt. The payables line
        // carries the supplier, because the database refuses a control-account posting
        // without its dimension.
        var lines = new List<PostingLineRequest>
        {
            new(payables.Id, nameof(PostingDirection.Debit), payment.Amount,
                payment.CurrencyCode, payment.FxRate,
                SupplierId: payment.SupplierId,
                Description: $"Payment to {payment.Supplier!.Name}"),
            new(payment.BankAccountId, nameof(PostingDirection.Credit), payment.Amount,
                payment.CurrencyCode, payment.FxRate,
                Description: $"Payment to {payment.Supplier.Name}"),
        };

        var docNo = await numbers.AllocateAsync(
            payment.LegalEntityId, DocumentType, payment.PaymentDate, ct);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                payment.LegalEntityId,
                payment.PaymentDate,
                lines,
                Memo: $"Payment {docNo} to {payment.Supplier.Name}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: payment.Id),
            ct);

        payment.DocNo = docNo;
        payment.State = DocumentState.Posted;
        payment.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Posted payment {DocNo} as entry {EntryNo}", docNo, entry.EntryNo);

        return await SummarisePaymentAsync(payment.Id, ct);
    }

    public async Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        var payments = await db.SupplierPayments
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.DocNo,
                p.PaymentDate,
                SupplierName = p.Supplier!.Name,
                p.CurrencyCode,
                p.Amount,
                p.State,
                p.JournalEntryId,
                // Reversing rows carry a negative amount, so one sum over everything gives
                // the net -- no need to add and subtract two sets.
                Allocated = db.PaymentAllocations
                    .Where(a => a.SupplierPaymentId == p.Id)
                    .Sum(a => (decimal?)a.Amount) ?? 0m,
            })
            .ToListAsync(ct);

        return payments
            .Select(p => new PaymentSummary(
                p.Id, p.DocNo, p.PaymentDate, p.SupplierName, p.CurrencyCode,
                p.Amount, p.Allocated, p.Amount - p.Allocated,
                p.State.ToString(), p.JournalEntryId))
            .ToList();
    }

    // ---------------------------------------------------------------- allocation

    public async Task<IReadOnlyList<PaymentAllocationDetail>> AllocateAsync(
        AllocatePaymentRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var payment = await db.SupplierPayments
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct)
            ?? throw new NotFoundException($"No payment with id {request.PaymentId}.");

        if (payment.State != DocumentState.Posted)
        {
            throw new PostingValidationException(
                "A draft payment is not in the books yet, so it cannot settle anything. Post it first.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("Nothing to allocate.");
        }

        var alreadyAllocated = await NetAllocatedForPaymentAsync(payment.Id, ct);
        var available = payment.Amount - alreadyAllocated;
        var requested = request.Lines.Sum(l => l.Amount);

        if (requested > available)
        {
            throw new PostingValidationException(
                $"Payment {payment.DocNo} has {available:N2} {payment.CurrencyCode} unallocated "
                + $"but {requested:N2} was requested. Allocating more than was paid would "
                + "invent money.");
        }

        var results = new List<PaymentAllocation>();

        foreach (var line in request.Lines)
        {
            if (line.Amount <= 0)
            {
                throw new PostingValidationException("Every allocation must be a positive amount.");
            }

            var invoice = await db.PurchaseInvoices
                .FirstOrDefaultAsync(i => i.Id == line.PurchaseInvoiceId, ct)
                ?? throw new NotFoundException($"No purchase invoice with id {line.PurchaseInvoiceId}.");

            if (invoice.State != DocumentState.Posted)
            {
                throw new PostingValidationException(
                    "A draft bill is not owed yet, so nothing can be applied to it.");
            }

            if (invoice.SupplierId != payment.SupplierId)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.SupplierInvoiceNo} belongs to a different supplier than "
                    + $"payment {payment.DocNo}. Applying money paid to one supplier against "
                    + "another's bill hides two errors at once.");
            }

            if (invoice.CurrencyCode != payment.CurrencyCode)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.SupplierInvoiceNo} is in {invoice.CurrencyCode} and the "
                    + $"payment is in {payment.CurrencyCode}. Cross-currency settlement needs a "
                    + "conversion decision that is not this operation's to make.");
            }

            var outstanding = await OutstandingOnInvoiceAsync(invoice, ct);

            if (line.Amount > outstanding)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.SupplierInvoiceNo} has {outstanding:N2} outstanding but "
                    + $"{line.Amount:N2} was applied to it. Overpaying a bill leaves a debit on "
                    + "the supplier's account, which is a separate decision.");
            }

            // The payable was carried at the invoice's rate and is being settled at the
            // payment's. Positive means more was owed than was paid — a gain, the opposite
            // sign to the receivables case, because a payable is a credit balance.
            var fxDifference = decimal.Round(
                line.Amount * (invoice.FxRate - payment.FxRate), 4, MidpointRounding.ToEven);

            Guid? fxEntryId = null;

            if (fxDifference != 0)
            {
                fxEntryId = await PostExchangeDifferenceAsync(payment, invoice, fxDifference, ct);
            }

            var allocation = new PaymentAllocation
            {
                Id = Guid.NewGuid(),
                TenantId = payment.TenantId,
                LegalEntityId = payment.LegalEntityId,
                SupplierPaymentId = payment.Id,
                PurchaseInvoiceId = invoice.Id,
                Amount = line.Amount,
                FunctionalAmount = decimal.Round(
                    line.Amount * payment.FxRate, 4, MidpointRounding.ToEven),
                FxGainLossFunctional = fxDifference,
                JournalEntryId = fxEntryId,
                AllocatedAtUtc = DateTimeOffset.UtcNow,
                AllocatedByUserId = userId,
            };

            db.PaymentAllocations.Add(allocation);
            results.Add(allocation);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await DescribeAllocationsAsync(results.Select(a => a.Id).ToList(), ct);
    }

    public async Task<PaymentAllocationDetail> UnallocateAsync(
        Guid allocationId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var original = await db.PaymentAllocations
            .FirstOrDefaultAsync(a => a.Id == allocationId, ct)
            ?? throw new NotFoundException($"No allocation with id {allocationId}.");

        if (original.ReversesAllocationId is not null)
        {
            throw new PostingValidationException("That row is itself a reversal.");
        }

        var alreadyReversed = await db.PaymentAllocations
            .AnyAsync(a => a.ReversesAllocationId == allocationId, ct);

        if (alreadyReversed)
        {
            throw new PostingValidationException("That allocation has already been undone.");
        }

        // A reversing row rather than a delete: which bill a payment cleared is a fact worth
        // keeping, and a supplier querying it deserves a record.
        var reversal = new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            TenantId = original.TenantId,
            LegalEntityId = original.LegalEntityId,
            SupplierPaymentId = original.SupplierPaymentId,
            PurchaseInvoiceId = original.PurchaseInvoiceId,
            Amount = -original.Amount,
            FunctionalAmount = -original.FunctionalAmount,
            FxGainLossFunctional = -original.FxGainLossFunctional,
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            AllocatedByUserId = userId,
            ReversesAllocationId = original.Id,
        };

        db.PaymentAllocations.Add(reversal);
        await db.SaveChangesAsync(ct);

        return (await DescribeAllocationsAsync([reversal.Id], ct))[0];
    }

    // ---------------------------------------------------------------- reporting

    public async Task<IReadOnlyList<OpenPurchaseInvoice>> GetOpenInvoicesAsync(
        Guid legalEntityId, Guid? supplierId, CancellationToken ct = default)
    {
        var invoices = await db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.LegalEntityId == legalEntityId && i.State == DocumentState.Posted)
            .Where(i => supplierId == null || i.SupplierId == supplierId)
            .ToListAsync(ct);

        var allocated = await db.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.LegalEntityId == legalEntityId)
            .GroupBy(a => a.PurchaseInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Allocated, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return invoices
            .Select(i =>
            {
                var applied = allocated.GetValueOrDefault(i.Id, 0m);
                var gross = i.TotalWithTax;
                return new OpenPurchaseInvoice(
                    i.Id, i.DocNo, i.SupplierInvoiceNo, i.DocDate, i.DueDate, i.CurrencyCode,
                    gross, applied, gross - applied,
                    Math.Max(0, today.DayNumber - i.DueDate.DayNumber));
            })
            .Where(i => i.Outstanding > 0)
            .OrderBy(i => i.DueDate)
            .ToList();
    }

    /// <summary>
    /// Ageing, computed from purchase invoices and what has been applied to them. The total
    /// must equal the payables control account balance, because both derive from the same
    /// postings.
    /// </summary>
    public async Task<PayablesAgeingReport> GetAgeingAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default)
    {
        var open = await GetOpenInvoicesAsync(legalEntityId, null, ct);

        var invoiceSuppliers = await db.PurchaseInvoices
            .AsNoTracking()
            .Where(i => i.LegalEntityId == legalEntityId)
            .Select(i => new { i.Id, i.SupplierId, i.Supplier!.Code, i.Supplier.Name })
            .ToListAsync(ct);

        var byInvoice = invoiceSuppliers.ToDictionary(x => x.Id);

        var grouped = open
            .Where(i => byInvoice.ContainsKey(i.Id))
            .GroupBy(i => byInvoice[i.Id].SupplierId)
            .Select(g =>
            {
                var first = byInvoice[g.First().Id];

                decimal Bucket(Func<int, bool> predicate) =>
                    g.Where(i => predicate(asOf.DayNumber - i.DueDate.DayNumber))
                     .Sum(i => i.Outstanding);

                return new SupplierBalance(
                    g.Key,
                    first.Code,
                    first.Name,
                    g.Sum(i => i.Outstanding),
                    Bucket(d => d <= 0),
                    Bucket(d => d is >= 1 and <= 30),
                    Bucket(d => d is >= 31 and <= 60),
                    Bucket(d => d is >= 61 and <= 90),
                    Bucket(d => d > 90));
            })
            .OrderBy(s => s.SupplierCode)
            .ToList();

        return new PayablesAgeingReport(asOf, grouped, grouped.Sum(s => s.Balance));
    }

    public async Task<SupplierStatement> GetStatementAsync(
        Guid legalEntityId, Guid supplierId, DateOnly asOf, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new NotFoundException($"No supplier with id {supplierId}.");

        var movements = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId
                        && p.SupplierId == supplierId
                        && p.Account!.ControlType == ControlType.AccountsPayable
                        && p.JournalEntry!.EntryDate <= asOf)
            .Select(p => new
            {
                p.JournalEntry!.EntryDate,
                p.JournalEntry.SourceDocumentType,
                p.JournalEntry.EntryNo,
                p.Description,
                p.Direction,
                p.FunctionalAmount,
            })
            .ToListAsync(ct);

        // A payable is a credit balance, so the running total is credits less debits — the
        // mirror of the customer statement, and it reads positive when money is owed.
        var running = 0m;
        var lines = movements
            .OrderBy(m => m.EntryDate)
            .ThenBy(m => m.EntryNo)
            .Select(m =>
            {
                var debit = m.Direction == PostingDirection.Debit ? m.FunctionalAmount : 0m;
                var credit = m.Direction == PostingDirection.Credit ? m.FunctionalAmount : 0m;
                running += credit - debit;
                return new SupplierStatementLine(
                    m.EntryDate, m.SourceDocumentType, m.EntryNo, m.Description,
                    debit, credit, running);
            })
            .ToList();

        return new SupplierStatement(supplierId, supplier.Name, asOf, lines, running);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Account> ResolvePayablesAccountAsync(Guid tenantId, CancellationToken ct)
        => await db.Accounts
            .Where(a => a.TenantId == tenantId
                        && a.ControlType == ControlType.AccountsPayable
                        && a.IsPostable && a.IsActive)
            .OrderBy(a => a.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "The chart of accounts has no active payables control account.");

    private async Task<decimal> NetAllocatedForPaymentAsync(Guid paymentId, CancellationToken ct)
        => await db.PaymentAllocations
            .Where(a => a.SupplierPaymentId == paymentId)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

    /// <summary>
    /// What is still payable on a bill: gross of tax, less what has been applied.
    /// </summary>
    /// <remarks>
    /// Gross, because tax is part of what the supplier is paid — including the reclaimable
    /// part, which the business pays over and recovers from the authority separately.
    /// </remarks>
    private async Task<decimal> OutstandingOnInvoiceAsync(
        PurchaseInvoice invoice, CancellationToken ct)
    {
        var lines = await db.PurchaseInvoiceLines
            .AsNoTracking()
            .Where(l => l.PurchaseInvoiceId == invoice.Id)
            .ToListAsync(ct);

        var gross = lines.Sum(l => l.LineTotalWithTax);

        var allocated = await db.PaymentAllocations
            .Where(a => a.PurchaseInvoiceId == invoice.Id)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        return gross - allocated;
    }

    /// <summary>
    /// Posts the exchange difference realised by settling a foreign-currency bill.
    /// </summary>
    /// <remarks>
    /// The payment already cleared payables at its own rate, leaving the residue between the
    /// two rates on the control account. This clears it to the realised FX account so the
    /// supplier's balance reaches zero in both currencies.
    /// <para>
    /// The signs are the opposite of the receivables case. A payable is a credit balance, so a
    /// positive difference — more owed than paid — is a gain, and payables is debited to clear
    /// it down.
    /// </para>
    /// </remarks>
    private async Task<Guid> PostExchangeDifferenceAsync(
        SupplierPayment payment, PurchaseInvoice invoice, decimal difference, CancellationToken ct)
    {
        var payables = await ResolvePayablesAccountAsync(payment.TenantId, ct);

        var fxAccount = await db.Accounts
            .Where(a => a.TenantId == payment.TenantId
                        && a.SystemRole == AccountSystemRole.RealisedFxGainLoss
                        && a.IsPostable && a.IsActive)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "Settling this bill realises an exchange difference, but no account is marked "
                + "as the realised exchange gain/loss account.");

        var entity = await db.LegalEntities.FirstAsync(e => e.Id == payment.LegalEntityId, ct);
        var amount = Math.Abs(difference);

        var payablesSide = difference > 0
            ? nameof(PostingDirection.Debit)
            : nameof(PostingDirection.Credit);
        var fxSide = difference > 0
            ? nameof(PostingDirection.Credit)
            : nameof(PostingDirection.Debit);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                payment.LegalEntityId,
                payment.PaymentDate,
                [
                    new PostingLineRequest(
                        payables.Id, payablesSide, amount,
                        entity.FunctionalCurrency, 1m,
                        SupplierId: payment.SupplierId,
                        Description: $"Exchange difference on {invoice.SupplierInvoiceNo}"),
                    new PostingLineRequest(
                        fxAccount.Id, fxSide, amount,
                        entity.FunctionalCurrency, 1m,
                        Description: $"Exchange difference on {invoice.SupplierInvoiceNo}"),
                ],
                Memo: $"Realised exchange difference settling {invoice.SupplierInvoiceNo}",
                SourceDocumentType: "ExchangeDifference",
                SourceDocumentId: invoice.Id),
            ct);

        return entry.Id;
    }

    private async Task<PaymentSummary> SummarisePaymentAsync(Guid paymentId, CancellationToken ct)
    {
        var entityId = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => p.LegalEntityId)
            .FirstAsync(ct);

        var all = await ListPaymentsAsync(entityId, ct);
        return all.First(p => p.Id == paymentId);
    }

    private async Task<IReadOnlyList<PaymentAllocationDetail>> DescribeAllocationsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
        => await db.PaymentAllocations
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new PaymentAllocationDetail(
                a.Id,
                a.SupplierPaymentId,
                a.SupplierPayment!.DocNo,
                a.PurchaseInvoiceId,
                a.PurchaseInvoice!.DocNo,
                a.Amount,
                a.FunctionalAmount,
                a.FxGainLossFunctional,
                a.JournalEntryId,
                a.AllocatedAtUtc,
                a.ReversesAllocationId))
            .ToListAsync(ct);
}

// ---------------------------------------------------------------- suppliers

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierSummary>> ListAsync(CancellationToken ct = default);
}

/// <summary>Read-only for now, matching <c>CustomerService</c>.</summary>
public sealed class SupplierService(AccountingDbContext db) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierSummary>> ListAsync(CancellationToken ct = default)
        => await db.Suppliers
            .AsNoTracking()
            .OrderBy(s => s.Code)
            .Select(s => new SupplierSummary(
                s.Id, s.Code, s.Name, s.CurrencyCode, s.CreditTermDays, s.IsActive))
            .ToListAsync(ct);
}
