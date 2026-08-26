using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IPurchaseInvoiceService
{
    Task<PurchaseInvoiceDetail> CreateDraftAsync(
        CreatePurchaseInvoiceRequest request, CancellationToken ct = default);

    Task<PurchaseInvoiceDetail> PostAsync(Guid invoiceId, CancellationToken ct = default);

    Task<PurchaseInvoiceDetail> GetAsync(Guid invoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<PurchaseInvoiceSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default);
}

/// <summary>
/// Supplier bills: drafting them, and posting them into the ledger.
/// </summary>
/// <remarks>
/// The mirror of <see cref="SalesInvoiceService"/>. The differences are the duplicate-bill
/// check and the reclaimability of tax, both of which have no sales-side equivalent.
/// </remarks>
public sealed class PurchaseInvoiceService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    PurchaseInvoicePostingRule rule,
    ILogger<PurchaseInvoiceService> logger) : IPurchaseInvoiceService
{
    private const string DocumentType = "PurchaseInvoice";

    public async Task<PurchaseInvoiceDetail> CreateDraftAsync(
        CreatePurchaseInvoiceRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var entity = await db.LegalEntities.FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
            ?? throw new NotFoundException($"No supplier with id {request.SupplierId}.");

        if (string.IsNullOrWhiteSpace(request.SupplierInvoiceNo))
        {
            throw new PostingValidationException(
                "The supplier's own invoice number is required. Without it the same bill "
                + "cannot be recognised if it arrives again.");
        }

        var reference = request.SupplierInvoiceNo.Trim();

        // Checked here so the caller gets a message naming the existing bill. The unique index
        // is what actually guarantees it -- two concurrent requests would both pass this test.
        var duplicate = await db.PurchaseInvoices
            .AsNoTracking()
            .Where(i => i.SupplierId == supplier.Id && i.SupplierInvoiceNo == reference)
            .Select(i => new { i.DocNo, i.DocDate, i.State })
            .FirstOrDefaultAsync(ct);

        if (duplicate is not null)
        {
            throw new PostingValidationException(
                $"{supplier.Name} invoice {reference} is already recorded"
                + (duplicate.DocNo is null ? " as a draft" : $" as {duplicate.DocNo}")
                + $", dated {duplicate.DocDate:yyyy-MM-dd}. Paying the same bill twice is much "
                + "harder to unpick than refusing it now.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("A purchase invoice needs at least one line.");
        }

        var chargeAccountIds = request.Lines.Select(l => l.ChargeAccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(a => chargeAccountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var accountId in chargeAccountIds)
        {
            if (!accounts.TryGetValue(accountId, out var account))
            {
                throw new NotFoundException($"No account with id {accountId}.");
            }

            if (!account.IsPostable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a heading and cannot be charged to.");
            }

            // A bill charged to receivables or payables would land on a control account
            // without its dimension and be refused at posting time. Saying so now is kinder.
            if (account.ControlType is ControlType.AccountsReceivable or ControlType.AccountsPayable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a control account for a "
                    + "subledger and cannot be charged directly.");
            }
        }

        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? supplier.CurrencyCode
            : request.CurrencyCode.ToUpperInvariant();

        var rate = request.FxRate ?? (currency == entity.FunctionalCurrency ? 1m : 0m);

        if (rate <= 0)
        {
            throw new PostingValidationException(
                $"The invoice is in {currency} but the entity's books are in "
                + $"{entity.FunctionalCurrency}, and no exchange rate was given.");
        }

        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            SupplierInvoiceNo = reference,
            DocDate = request.DocDate,
            DueDate = request.DueDate ?? request.DocDate.AddDays(supplier.CreditTermDays),
            SupplierId = supplier.Id,
            CurrencyCode = currency,
            FxRate = rate,
            Memo = request.Memo,
            State = DocumentState.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
        };

        var taxCodes = await ResolveTaxCodesAsync(request, invoice.DocDate, ct);

        var lineNo = 1;
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.UnitPrice <= 0)
            {
                throw new PostingValidationException(
                    $"Line {lineNo} ({line.Description}) has quantity {line.Quantity} at "
                    + $"{line.UnitPrice}. Both must be positive — a negative line is a "
                    + "supplier credit note.");
            }

            var code = line.TaxCodeId is null ? null : taxCodes[line.TaxCodeId.Value];

            invoice.Lines.Add(new PurchaseInvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = entity.TenantId,
                PurchaseInvoiceId = invoice.Id,
                LineNo = lineNo++,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                ChargeAccountId = line.ChargeAccountId,
                ProjectId = line.ProjectId,
                TaxCodeId = line.TaxCodeId,
                TaxRate = code?.Rate ?? 0m,
                // Copied now, not resolved at posting time. Whether the tax could be
                // reclaimed decides where it is posted, and a regime can be superseded --
                // Malaysia's GST was reclaimable and the SST replacing it is not.
                TaxReclaimable = code?.TaxRegime?.InputReclaimable ?? false,
            });
        }

        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return await GetAsync(invoice.Id, ct);
    }

    public async Task<PurchaseInvoiceDetail> PostAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var invoice = await db.PurchaseInvoices
            .Include(i => i.Lines)
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException($"No purchase invoice with id {invoiceId}.");

        if (invoice.State == DocumentState.Posted)
        {
            throw new PostingValidationException(
                $"Invoice {invoice.DocNo} is already posted. Posting is a one-way door — "
                + "record a supplier credit note to undo it.");
        }

        var payables = await ResolvePayablesAccountAsync(invoice.TenantId, ct);
        var inputTaxAccounts = await ResolveInputTaxAccountsAsync(invoice, ct);

        var lines = rule.Build(
            invoice, new PurchasePostingRuleContext(payables.Id, inputTaxAccounts));

        // Held in a local for the same reason the sales side does: PostAsync saves changes of
        // its own, which would flush a Draft row carrying a document number and no entry --
        // exactly what ck_purchase_invoice_posted_is_complete refuses.
        var docNo = await numbers.AllocateAsync(
            invoice.LegalEntityId, DocumentType, invoice.DocDate, ct);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                invoice.LegalEntityId,
                invoice.DocDate,
                lines,
                Memo: $"Invoice {invoice.SupplierInvoiceNo} from {invoice.Supplier!.Name}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: invoice.Id),
            ct);

        invoice.DocNo = docNo;
        invoice.State = DocumentState.Posted;
        invoice.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Posted purchase invoice {DocNo} ({SupplierRef}) as entry {EntryNo}",
            docNo, invoice.SupplierInvoiceNo, entry.EntryNo);

        return await GetAsync(invoice.Id, ct);
    }

    public async Task<PurchaseInvoiceDetail> GetAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.ChargeAccount)
            .Include(i => i.Lines).ThenInclude(l => l.TaxCode)
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException($"No purchase invoice with id {invoiceId}.");

        return new PurchaseInvoiceDetail(
            invoice.Id,
            invoice.DocNo,
            invoice.SupplierInvoiceNo,
            invoice.DocDate,
            invoice.DueDate,
            invoice.SupplierId,
            invoice.Supplier!.Code,
            invoice.Supplier.Name,
            invoice.CurrencyCode,
            invoice.FxRate,
            invoice.Memo,
            invoice.State.ToString(),
            invoice.JournalEntryId,
            invoice.Total,
            invoice.TaxTotal,
            invoice.TotalWithTax,
            invoice.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseInvoiceLineDetail(
                l.Id,
                l.LineNo,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.LineTotal,
                l.ChargeAccountId,
                l.ChargeAccount!.Code,
                l.ChargeAccount.Name,
                l.TaxCodeId,
                l.TaxCode is null ? null : $"{l.TaxCode.Code} — {l.TaxCode.Name}",
                l.TaxRate,
                l.TaxAmount,
                l.TaxReclaimable,
                l.ChargeAmount)).ToList());
    }

    public async Task<IReadOnlyList<PurchaseInvoiceSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        // Lines are materialised because tax rounds per line; that arithmetic lives in the
        // model and is not duplicated in SQL.
        var invoices = await db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Supplier)
            .Where(i => i.LegalEntityId == legalEntityId)
            .OrderByDescending(i => i.DocDate)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return invoices
            .Select(i => new PurchaseInvoiceSummary(
                i.Id,
                i.DocNo,
                i.SupplierInvoiceNo,
                i.DocDate,
                i.DueDate,
                i.Supplier!.Name,
                i.CurrencyCode,
                i.Total,
                i.TaxTotal,
                i.TotalWithTax,
                i.State.ToString(),
                i.JournalEntryId))
            .ToList();
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
                "The chart of accounts has no active payables control account, so a bill has "
                + "nowhere to credit.");

    /// <summary>
    /// Loads the tax codes a request refers to, with their regimes, as at the document date.
    /// </summary>
    /// <remarks>
    /// The regime is included because <c>InputReclaimable</c> lives there and decides where a
    /// line's tax is posted. Effective-dated on the document date rather than today, so a
    /// back-dated bill uses the regime that was actually in force.
    /// </remarks>
    private async Task<Dictionary<Guid, TaxCode>> ResolveTaxCodesAsync(
        CreatePurchaseInvoiceRequest request, DateOnly docDate, CancellationToken ct)
    {
        var ids = request.Lines
            .Where(l => l.TaxCodeId is not null)
            .Select(l => l.TaxCodeId!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var codes = await db.TaxCodes
            .Include(c => c.TaxRegime)
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        foreach (var id in ids)
        {
            if (!codes.TryGetValue(id, out var code))
            {
                throw new NotFoundException($"No tax code with id {id}.");
            }

            if (!code.AppliesOn(docDate))
            {
                throw new PostingValidationException(
                    $"Tax code {code.Code} ({code.Name}) does not apply on "
                    + $"{docDate:yyyy-MM-dd}. Use a code that was in force on that date.");
            }
        }

        return codes;
    }

    /// <summary>
    /// Where each reclaimable code's input tax is debited. Non-reclaimable codes are absent,
    /// because their tax goes to the charge account instead.
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> ResolveInputTaxAccountsAsync(
        PurchaseInvoice invoice, CancellationToken ct)
    {
        var ids = invoice.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0 && l.TaxReclaimable)
            .Select(l => l.TaxCodeId!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var codes = await db.TaxCodes.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var map = new Dictionary<Guid, Guid>();

        foreach (var code in codes)
        {
            if (code.InputAccountId is null)
            {
                throw new PostingValidationException(
                    $"Tax code {code.Code} carries reclaimable tax at {code.Rate}% but has no "
                    + "input tax account. Set one on the code, or mark its regime as not "
                    + "reclaimable so the tax is treated as part of the cost.");
            }

            map[code.Id] = code.InputAccountId.Value;
        }

        return map;
    }
}
