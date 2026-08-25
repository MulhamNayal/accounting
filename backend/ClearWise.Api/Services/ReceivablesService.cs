using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Services;

public interface IReceivablesService
{
    Task<ReceiptSummary> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default);

    Task<ReceiptSummary> PostReceiptAsync(Guid receiptId, CancellationToken ct = default);

    Task<IReadOnlyList<ReceiptSummary>> ListReceiptsAsync(Guid legalEntityId, CancellationToken ct = default);

    Task<IReadOnlyList<AllocationDetail>> AllocateAsync(AllocateRequest request, CancellationToken ct = default);

    Task<AllocationDetail> UnallocateAsync(Guid allocationId, CancellationToken ct = default);

    Task<IReadOnlyList<OpenInvoice>> GetOpenInvoicesAsync(
        Guid legalEntityId, Guid? customerId, CancellationToken ct = default);

    Task<AgeingReport> GetAgeingAsync(Guid legalEntityId, DateOnly asOf, CancellationToken ct = default);

    Task<CustomerStatement> GetStatementAsync(
        Guid legalEntityId, Guid customerId, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Receipts, allocation, and everything the receivables subledger reports.
/// </summary>
/// <remarks>
/// There is no AR ledger table. A customer's balance is the sum of postings to receivables
/// control accounts carrying their id — the very rows the control account itself is computed
/// from. The two therefore cannot drift apart, which is the whole reason the ledger has one
/// posting table rather than a general ledger plus subledgers that are supposed to agree.
/// </remarks>
public sealed class ReceivablesService(
    ClearWiseDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    ILogger<ReceivablesService> logger) : IReceivablesService
{
    private const string DocumentType = "CustomerReceipt";

    // ---------------------------------------------------------------- receipts

    public async Task<ReceiptSummary> CreateReceiptAsync(
        CreateReceiptRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var entity = await db.LegalEntities.FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException($"No customer with id {request.CustomerId}.");

        var bank = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.BankAccountId, ct)
            ?? throw new NotFoundException($"No account with id {request.BankAccountId}.");

        if (bank.ControlType != ControlType.Bank)
        {
            throw new PostingValidationException(
                $"Account {bank.Code} ({bank.Name}) is not a bank or cash account. "
                + "Money has to land somewhere that represents money.");
        }

        if (request.Amount <= 0)
        {
            throw new PostingValidationException(
                $"The receipt is for {request.Amount}. A refund to a customer is a payment, "
                + "not a negative receipt.");
        }

        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? customer.CurrencyCode
            : request.CurrencyCode.ToUpperInvariant();

        var rate = request.FxRate ?? (currency == entity.FunctionalCurrency ? 1m : 0m);

        if (rate <= 0)
        {
            throw new PostingValidationException(
                $"The receipt is in {currency} but the books are in {entity.FunctionalCurrency}, "
                + "and no exchange rate was given.");
        }

        var receipt = new CustomerReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            ReceiptDate = request.ReceiptDate,
            CustomerId = customer.Id,
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

        db.CustomerReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        return await SummariseReceiptAsync(receipt.Id, ct);
    }

    public async Task<ReceiptSummary> PostReceiptAsync(Guid receiptId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var receipt = await db.CustomerReceipts
            .Include(r => r.Customer)
            .Include(r => r.BankAccount)
            .FirstOrDefaultAsync(r => r.Id == receiptId, ct)
            ?? throw new NotFoundException($"No receipt with id {receiptId}.");

        if (receipt.State == DocumentState.Posted)
        {
            throw new PostingValidationException($"Receipt {receipt.DocNo} is already posted.");
        }

        var receivables = await ResolveReceivablesAccountAsync(receipt.TenantId, ct);

        // Debit the bank, credit receivables. The receivables line carries the customer,
        // because the database refuses a control-account posting without its dimension.
        var lines = new List<PostingLineRequest>
        {
            new(receipt.BankAccountId, nameof(PostingDirection.Debit), receipt.Amount,
                receipt.CurrencyCode, receipt.FxRate,
                Description: $"Receipt from {receipt.Customer!.Name}"),
            new(receivables.Id, nameof(PostingDirection.Credit), receipt.Amount,
                receipt.CurrencyCode, receipt.FxRate,
                CustomerId: receipt.CustomerId,
                Description: $"Receipt from {receipt.Customer.Name}"),
        };

        var docNo = await numbers.AllocateAsync(
            receipt.LegalEntityId, DocumentType, receipt.ReceiptDate, ct);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                receipt.LegalEntityId,
                receipt.ReceiptDate,
                lines,
                Memo: $"Receipt {docNo} from {receipt.Customer.Name}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: receipt.Id),
            ct);

        receipt.DocNo = docNo;
        receipt.State = DocumentState.Posted;
        receipt.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Posted receipt {DocNo} as entry {EntryNo}", docNo, entry.EntryNo);

        return await SummariseReceiptAsync(receipt.Id, ct);
    }

    public async Task<IReadOnlyList<ReceiptSummary>> ListReceiptsAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        var receipts = await db.CustomerReceipts
            .AsNoTracking()
            .Where(r => r.LegalEntityId == legalEntityId)
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.DocNo,
                r.ReceiptDate,
                CustomerName = r.Customer!.Name,
                r.CurrencyCode,
                r.Amount,
                r.State,
                r.JournalEntryId,
                Allocated = db.Allocations
                    .Where(a => a.CustomerReceiptId == r.Id && a.ReversesAllocationId == null)
                    .Sum(a => (decimal?)a.Amount) ?? 0m,
                Reversed = db.Allocations
                    .Where(a => a.CustomerReceiptId == r.Id && a.ReversesAllocationId != null)
                    .Sum(a => (decimal?)a.Amount) ?? 0m,
            })
            .ToListAsync(ct);

        return receipts
            .Select(r =>
            {
                // A reversing row carries a negative amount, so net allocation is the sum
                // of both sets rather than one minus the other.
                var allocated = r.Allocated + r.Reversed;
                return new ReceiptSummary(
                    r.Id, r.DocNo, r.ReceiptDate, r.CustomerName, r.CurrencyCode,
                    r.Amount, allocated, r.Amount - allocated,
                    r.State.ToString(), r.JournalEntryId);
            })
            .ToList();
    }

    // ---------------------------------------------------------------- allocation

    public async Task<IReadOnlyList<AllocationDetail>> AllocateAsync(
        AllocateRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var receipt = await db.CustomerReceipts
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == request.ReceiptId, ct)
            ?? throw new NotFoundException($"No receipt with id {request.ReceiptId}.");

        if (receipt.State != DocumentState.Posted)
        {
            throw new PostingValidationException(
                "A draft receipt is not in the books yet, so it cannot settle anything. Post it first.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("Nothing to allocate.");
        }

        var alreadyAllocated = await NetAllocatedForReceiptAsync(receipt.Id, ct);
        var available = receipt.Amount - alreadyAllocated;
        var requested = request.Lines.Sum(l => l.Amount);

        if (requested > available)
        {
            throw new PostingValidationException(
                $"Receipt {receipt.DocNo} has {available:N2} {receipt.CurrencyCode} unallocated "
                + $"but {requested:N2} was requested. Allocating more than was received would "
                + "invent money.");
        }

        var results = new List<Allocation>();

        foreach (var line in request.Lines)
        {
            if (line.Amount <= 0)
            {
                throw new PostingValidationException("Every allocation must be a positive amount.");
            }

            var invoice = await db.SalesInvoices
                .FirstOrDefaultAsync(i => i.Id == line.SalesInvoiceId, ct)
                ?? throw new NotFoundException($"No invoice with id {line.SalesInvoiceId}.");

            if (invoice.State != DocumentState.Posted)
            {
                throw new PostingValidationException(
                    "A draft invoice is not owed yet, so nothing can be applied to it.");
            }

            if (invoice.CustomerId != receipt.CustomerId)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.DocNo} belongs to a different customer than receipt "
                    + $"{receipt.DocNo}. Applying one customer's money to another's debt hides "
                    + "two errors at once.");
            }

            if (invoice.CurrencyCode != receipt.CurrencyCode)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.DocNo} is in {invoice.CurrencyCode} and the receipt is in "
                    + $"{receipt.CurrencyCode}. Cross-currency settlement needs a conversion "
                    + "decision that is not this operation's to make.");
            }

            var outstanding = await OutstandingOnInvoiceAsync(invoice, ct);

            if (line.Amount > outstanding)
            {
                throw new PostingValidationException(
                    $"Invoice {invoice.DocNo} has {outstanding:N2} outstanding but "
                    + $"{line.Amount:N2} was applied to it. Overpaying an invoice leaves a "
                    + "credit on the account, which is a separate decision.");
            }

            // Realised exchange difference: the receivable was carried at the invoice's rate
            // and is being settled at the receipt's. The gap is a real gain or loss now.
            var fxDifference = decimal.Round(
                line.Amount * (invoice.FxRate - receipt.FxRate), 4, MidpointRounding.ToEven);

            Guid? fxEntryId = null;

            if (fxDifference != 0)
            {
                fxEntryId = await PostExchangeDifferenceAsync(
                    receipt, invoice, fxDifference, ct);
            }

            var allocation = new Allocation
            {
                Id = Guid.NewGuid(),
                TenantId = receipt.TenantId,
                LegalEntityId = receipt.LegalEntityId,
                CustomerReceiptId = receipt.Id,
                SalesInvoiceId = invoice.Id,
                Amount = line.Amount,
                FunctionalAmount = decimal.Round(
                    line.Amount * receipt.FxRate, 4, MidpointRounding.ToEven),
                FxGainLossFunctional = fxDifference,
                JournalEntryId = fxEntryId,
                AllocatedAtUtc = DateTimeOffset.UtcNow,
                AllocatedByUserId = userId,
            };

            db.Allocations.Add(allocation);
            results.Add(allocation);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await DescribeAllocationsAsync(results.Select(a => a.Id).ToList(), ct);
    }

    public async Task<AllocationDetail> UnallocateAsync(Guid allocationId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var original = await db.Allocations
            .FirstOrDefaultAsync(a => a.Id == allocationId, ct)
            ?? throw new NotFoundException($"No allocation with id {allocationId}.");

        if (original.ReversesAllocationId is not null)
        {
            throw new PostingValidationException("That row is itself a reversal.");
        }

        var alreadyReversed = await db.Allocations
            .AnyAsync(a => a.ReversesAllocationId == allocationId, ct);

        if (alreadyReversed)
        {
            throw new PostingValidationException("That allocation has already been undone.");
        }

        // A reversing row rather than a delete: how money was applied is a fact, and a
        // customer disputing which invoice their payment cleared deserves a record.
        var reversal = new Allocation
        {
            Id = Guid.NewGuid(),
            TenantId = original.TenantId,
            LegalEntityId = original.LegalEntityId,
            CustomerReceiptId = original.CustomerReceiptId,
            SalesInvoiceId = original.SalesInvoiceId,
            Amount = -original.Amount,
            FunctionalAmount = -original.FunctionalAmount,
            FxGainLossFunctional = -original.FxGainLossFunctional,
            AllocatedAtUtc = DateTimeOffset.UtcNow,
            AllocatedByUserId = userId,
            ReversesAllocationId = original.Id,
        };

        db.Allocations.Add(reversal);
        await db.SaveChangesAsync(ct);

        return (await DescribeAllocationsAsync([reversal.Id], ct))[0];
    }

    // ---------------------------------------------------------------- reporting

    public async Task<IReadOnlyList<OpenInvoice>> GetOpenInvoicesAsync(
        Guid legalEntityId, Guid? customerId, CancellationToken ct = default)
    {
        // Lines are loaded rather than aggregated in SQL because tax rounds per line, and
        // that arithmetic should exist in exactly one place.
        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.LegalEntityId == legalEntityId && i.State == DocumentState.Posted)
            .Where(i => customerId == null || i.CustomerId == customerId)
            .ToListAsync(ct);

        var allocationsByInvoice = await db.Allocations
            .AsNoTracking()
            .Where(a => a.LegalEntityId == legalEntityId)
            .GroupBy(a => a.SalesInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Allocated, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return invoices
            .Select(i =>
            {
                var allocated = allocationsByInvoice.GetValueOrDefault(i.Id, 0m);
                var gross = i.TotalWithTax;
                return new OpenInvoice(
                    i.Id, i.DocNo, i.DocDate, i.DueDate, i.CurrencyCode,
                    gross, allocated, gross - allocated,
                    Math.Max(0, today.DayNumber - i.DueDate.DayNumber));
            })
            .Where(i => i.Outstanding > 0)
            .OrderBy(i => i.DueDate)
            .ToList();
    }

    /// <summary>
    /// Ageing, computed from receivables postings. The total must equal the receivables
    /// control account balance, because both are the same rows summed differently.
    /// </summary>
    public async Task<AgeingReport> GetAgeingAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default)
    {
        var open = await GetOpenInvoicesAsync(legalEntityId, null, ct);

        var invoiceCustomers = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => i.LegalEntityId == legalEntityId)
            .Select(i => new { i.Id, i.CustomerId, i.Customer!.Code, i.Customer.Name })
            .ToListAsync(ct);

        var byInvoice = invoiceCustomers.ToDictionary(x => x.Id);

        var grouped = open
            .Where(i => byInvoice.ContainsKey(i.Id))
            .GroupBy(i => byInvoice[i.Id].CustomerId)
            .Select(g =>
            {
                var first = byInvoice[g.First().Id];

                decimal Bucket(Func<int, bool> predicate) =>
                    g.Where(i => predicate(asOf.DayNumber - i.DueDate.DayNumber))
                     .Sum(i => i.Outstanding);

                return new CustomerBalance(
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
            .OrderBy(c => c.CustomerCode)
            .ToList();

        return new AgeingReport(asOf, grouped, grouped.Sum(c => c.Balance));
    }

    public async Task<CustomerStatement> GetStatementAsync(
        Guid legalEntityId, Guid customerId, DateOnly asOf, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException($"No customer with id {customerId}.");

        // Straight from the ledger: every receivables posting carrying this customer.
        var movements = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId
                        && p.CustomerId == customerId
                        && p.Account!.ControlType == ControlType.AccountsReceivable
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

        var running = 0m;
        var lines = movements
            .OrderBy(m => m.EntryDate)
            .ThenBy(m => m.EntryNo)
            .Select(m =>
            {
                var debit = m.Direction == PostingDirection.Debit ? m.FunctionalAmount : 0m;
                var credit = m.Direction == PostingDirection.Credit ? m.FunctionalAmount : 0m;
                running += debit - credit;
                return new StatementLine(
                    m.EntryDate, m.SourceDocumentType, m.EntryNo, m.Description,
                    debit, credit, running);
            })
            .ToList();

        return new CustomerStatement(customerId, customer.Name, asOf, lines, running);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Account> ResolveReceivablesAccountAsync(Guid tenantId, CancellationToken ct)
        => await db.Accounts
            .Where(a => a.TenantId == tenantId
                        && a.ControlType == ControlType.AccountsReceivable
                        && a.IsPostable && a.IsActive)
            .OrderBy(a => a.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "The chart of accounts has no active receivables control account.");

    private async Task<decimal> NetAllocatedForReceiptAsync(Guid receiptId, CancellationToken ct)
        => await db.Allocations
            .Where(a => a.CustomerReceiptId == receiptId)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

    /// <summary>
    /// What is still owed on an invoice: gross of tax, less what has been applied.
    /// </summary>
    /// <remarks>
    /// Gross, because tax is part of what the customer pays. Netting against the tax-exclusive
    /// total would leave every fully-settled invoice looking short by its tax.
    /// <para>
    /// The lines are materialised rather than summed in SQL because tax is rounded per line;
    /// keeping that arithmetic in the model avoids a second, subtly different implementation.
    /// </para>
    /// </remarks>
    private async Task<decimal> OutstandingOnInvoiceAsync(SalesInvoice invoice, CancellationToken ct)
    {
        var lines = await db.SalesInvoiceLines
            .AsNoTracking()
            .Where(l => l.SalesInvoiceId == invoice.Id)
            .ToListAsync(ct);

        var gross = lines.Sum(l => l.LineTotalWithTax);

        var allocated = await db.Allocations
            .Where(a => a.SalesInvoiceId == invoice.Id)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        return gross - allocated;
    }

    /// <summary>
    /// Posts the exchange difference realised by a settlement.
    /// </summary>
    /// <remarks>
    /// The receipt already cleared receivables at its own rate, which leaves the residue
    /// between the two rates sitting on the control account. This clears that residue to
    /// the realised FX account, so the customer's balance goes to zero in both currencies.
    /// </remarks>
    private async Task<Guid> PostExchangeDifferenceAsync(
        CustomerReceipt receipt, SalesInvoice invoice, decimal difference, CancellationToken ct)
    {
        var receivables = await ResolveReceivablesAccountAsync(receipt.TenantId, ct);

        var fxAccount = await db.Accounts
            .Where(a => a.TenantId == receipt.TenantId
                        && a.SystemRole == AccountSystemRole.RealisedFxGainLoss
                        && a.IsPostable && a.IsActive)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "Settling this invoice realises an exchange difference, but no account is "
                + "marked as the realised exchange gain/loss account.");

        var entity = await db.LegalEntities.FirstAsync(e => e.Id == receipt.LegalEntityId, ct);
        var amount = Math.Abs(difference);

        // difference > 0: the receivable was carried at more than was realised — a loss, so
        // receivables is credited down and the loss is debited.
        var receivablesSide = difference > 0
            ? nameof(PostingDirection.Credit)
            : nameof(PostingDirection.Debit);
        var fxSide = difference > 0
            ? nameof(PostingDirection.Debit)
            : nameof(PostingDirection.Credit);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                receipt.LegalEntityId,
                receipt.ReceiptDate,
                [
                    new PostingLineRequest(
                        receivables.Id, receivablesSide, amount,
                        entity.FunctionalCurrency, 1m,
                        CustomerId: receipt.CustomerId,
                        Description: $"Exchange difference on {invoice.DocNo}"),
                    new PostingLineRequest(
                        fxAccount.Id, fxSide, amount,
                        entity.FunctionalCurrency, 1m,
                        Description: $"Exchange difference on {invoice.DocNo}"),
                ],
                Memo: $"Realised exchange difference settling {invoice.DocNo}",
                SourceDocumentType: "ExchangeDifference",
                SourceDocumentId: invoice.Id),
            ct);

        return entry.Id;
    }

    private async Task<ReceiptSummary> SummariseReceiptAsync(Guid receiptId, CancellationToken ct)
    {
        var all = await ListReceiptsAsync(
            (await db.CustomerReceipts.AsNoTracking()
                .Where(r => r.Id == receiptId)
                .Select(r => r.LegalEntityId)
                .FirstAsync(ct)),
            ct);

        return all.First(r => r.Id == receiptId);
    }

    private async Task<IReadOnlyList<AllocationDetail>> DescribeAllocationsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
        => await db.Allocations
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new AllocationDetail(
                a.Id,
                a.CustomerReceiptId,
                a.CustomerReceipt!.DocNo,
                a.SalesInvoiceId,
                a.SalesInvoice!.DocNo,
                a.Amount,
                a.FunctionalAmount,
                a.FxGainLossFunctional,
                a.JournalEntryId,
                a.AllocatedAtUtc,
                a.ReversesAllocationId))
            .ToListAsync(ct);
}
