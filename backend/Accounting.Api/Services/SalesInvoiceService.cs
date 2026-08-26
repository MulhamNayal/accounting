using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface ISalesInvoiceService
{
    Task<SalesInvoiceDetail> CreateDraftAsync(CreateSalesInvoiceRequest request, CancellationToken ct = default);

    Task<SalesInvoiceDetail> PostAsync(Guid invoiceId, CancellationToken ct = default);

    Task<SalesInvoiceDetail> GetAsync(Guid invoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<SalesInvoiceSummary>> ListAsync(Guid legalEntityId, CancellationToken ct = default);
}

public sealed class SalesInvoiceService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    SalesInvoicePostingRule rule,
    ILogger<SalesInvoiceService> logger) : ISalesInvoiceService
{
    private const string DocumentType = "SalesInvoice";

    public async Task<SalesInvoiceDetail> CreateDraftAsync(
        CreateSalesInvoiceRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var entity = await db.LegalEntities.FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException($"No customer with id {request.CustomerId}.");

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("An invoice needs at least one line.");
        }

        var revenueAccountIds = request.Lines.Select(l => l.RevenueAccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(a => revenueAccountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var accountId in revenueAccountIds)
        {
            if (!accounts.TryGetValue(accountId, out var account))
            {
                throw new NotFoundException($"No account with id {accountId}.");
            }

            if (!account.IsPostable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a heading and cannot be invoiced to.");
            }
        }

        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? customer.CurrencyCode
            : request.CurrencyCode.ToUpperInvariant();

        var rate = request.FxRate ?? (currency == entity.FunctionalCurrency ? 1m : 0m);

        if (rate <= 0)
        {
            throw new PostingValidationException(
                $"The invoice is in {currency} but the entity's books are in "
                + $"{entity.FunctionalCurrency}, and no exchange rate was given.");
        }

        var invoice = new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            DocDate = request.DocDate,
            DueDate = request.DueDate ?? request.DocDate.AddDays(customer.CreditTermDays),
            CustomerId = customer.Id,
            CurrencyCode = currency,
            FxRate = rate,
            Reference = request.Reference,
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
                    + $"{line.UnitPrice}. Both must be positive â€” a negative line is a credit note.");
            }

            invoice.Lines.Add(new SalesInvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = entity.TenantId,
                SalesInvoiceId = invoice.Id,
                LineNo = lineNo++,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                RevenueAccountId = line.RevenueAccountId,
                ProjectId = line.ProjectId,
                AgentId = line.AgentId,
                TaxCodeId = line.TaxCodeId,
                // The rate is copied, not referenced. A code can be retired and replaced,
                // and this line must always reproduce the tax it actually charged.
                TaxRate = line.TaxCodeId is null ? 0m : taxCodes[line.TaxCodeId.Value].Rate,
            });
        }

        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return await GetAsync(invoice.Id, ct);
    }

    /// <summary>
    /// Allocates the document number, runs the posting rule, and writes the entry â€” all in
    /// one transaction.
    /// </summary>
    /// <remarks>
    /// The number is gapless, so it must be taken inside the transaction that commits the
    /// invoice. If anything downstream fails â€” an unbalanced rule output, a closed period â€”
    /// the number goes back and the next invoice takes it, leaving no hole for an auditor
    /// to ask about.
    /// </remarks>
    public async Task<SalesInvoiceDetail> PostAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var invoice = await db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException($"No sales invoice with id {invoiceId}.");

        if (invoice.State == DocumentState.Posted)
        {
            throw new PostingValidationException(
                $"Invoice {invoice.DocNo} is already posted. Posting is a one-way door â€” "
                + "issue a credit note to undo it.");
        }

        var receivables = await db.Accounts
            .Where(a => a.TenantId == invoice.TenantId
                        && a.ControlType == ControlType.AccountsReceivable
                        && a.IsPostable
                        && a.IsActive)
            .OrderBy(a => a.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "The chart of accounts has no active receivables control account, so an "
                + "invoice has nowhere to debit.");

        var outputTaxAccounts = await ResolveOutputTaxAccountsAsync(invoice, ct);

        var lines = rule.Build(
            invoice, new PostingRuleContext(receivables.Id, outputTaxAccounts));

        // Held in a local rather than assigned straight to the entity. PostAsync saves
        // changes of its own, which would flush this invoice mid-flight â€” a Draft row
        // carrying a document number but no entry, which ck_sales_invoice_posted_is_complete
        // rightly refuses. The three fields that define "posted" are set together, below.
        var docNo = await numbers.AllocateAsync(
            invoice.LegalEntityId, DocumentType, invoice.DocDate, ct);

        // Joins the transaction opened above rather than starting its own, so the entry and
        // the invoice's posted state are committed together or not at all.
        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                invoice.LegalEntityId,
                invoice.DocDate,
                lines,
                Memo: $"Invoice {docNo} to {invoice.Customer!.Name}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: invoice.Id),
            ct);

        invoice.DocNo = docNo;
        invoice.State = DocumentState.Posted;
        invoice.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Posted invoice {DocNo} as entry {EntryNo}", docNo, entry.EntryNo);

        return await GetAsync(invoice.Id, ct);
    }

    public async Task<SalesInvoiceDetail> GetAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.SalesInvoices
            .AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.RevenueAccount)
            .Include(i => i.Lines).ThenInclude(l => l.TaxCode)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException($"No sales invoice with id {invoiceId}.");

        return new SalesInvoiceDetail(
            invoice.Id,
            invoice.DocNo,
            invoice.DocDate,
            invoice.DueDate,
            invoice.CustomerId,
            invoice.Customer!.Code,
            invoice.Customer.Name,
            invoice.CurrencyCode,
            invoice.FxRate,
            invoice.Reference,
            invoice.Memo,
            invoice.State.ToString(),
            invoice.JournalEntryId,
            invoice.Total,
            invoice.TaxTotal,
            invoice.TotalWithTax,
            invoice.Lines.OrderBy(l => l.LineNo).Select(l => new SalesInvoiceLineDetail(
                l.Id,
                l.LineNo,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.LineTotal,
                l.RevenueAccountId,
                l.RevenueAccount!.Code,
                l.RevenueAccount.Name,
                l.TaxCodeId,
                l.TaxCode is null ? null : $"{l.TaxCode.Code} â€” {l.TaxCode.Name}",
                l.TaxRate,
                l.TaxAmount)).ToList());
    }

    public async Task<IReadOnlyList<SalesInvoiceSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        // Tax is rounded per line, so the totals are summed from the lines rather than
        // recomputed from the net â€” projecting the raw columns and finishing in memory keeps
        // that arithmetic in one place (the model) instead of duplicating it in SQL.
        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Where(i => i.LegalEntityId == legalEntityId)
            .OrderByDescending(i => i.DocDate)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return invoices
            .Select(i => new SalesInvoiceSummary(
                i.Id,
                i.DocNo,
                i.DocDate,
                i.DueDate,
                i.Customer!.Name,
                i.CurrencyCode,
                i.Total,
                i.TaxTotal,
                i.TotalWithTax,
                i.State.ToString(),
                i.JournalEntryId))
            .ToList();
    }

    /// <summary>
    /// Loads and validates the tax codes a request refers to, as at the document's date.
    /// </summary>
    /// <remarks>
    /// Effective-dated on the <em>document</em> date, not today. Back-dating an invoice into
    /// a period when a different regime was in force must use that regime's codes, which is
    /// how Malaysia's GST-to-SST transition survives without restating history.
    /// </remarks>
    private async Task<Dictionary<Guid, TaxCode>> ResolveTaxCodesAsync(
        CreateSalesInvoiceRequest request, DateOnly docDate, CancellationToken ct)
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

    private async Task<Dictionary<Guid, Guid>> ResolveOutputTaxAccountsAsync(
        SalesInvoice invoice, CancellationToken ct)
    {
        var ids = invoice.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0)
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
            if (code.OutputAccountId is null)
            {
                throw new PostingValidationException(
                    $"Tax code {code.Code} charges {code.Rate}% but has no output tax "
                    + "account. Set one on the code before invoicing with it.");
            }

            map[code.Id] = code.OutputAccountId.Value;
        }

        return map;
    }
}
