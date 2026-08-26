using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface ISalesCreditNoteService
{
    Task<SalesCreditNoteDetail> CreateDraftAsync(
        CreateSalesCreditNoteRequest request, CancellationToken ct = default);

    Task<SalesCreditNoteDetail> PostAsync(Guid noteId, CancellationToken ct = default);

    Task<SalesCreditNoteDetail> GetAsync(Guid noteId, CancellationToken ct = default);

    Task<IReadOnlyList<SalesCreditNoteSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default);
}

/// <summary>
/// Credits issued to customers.
/// </summary>
/// <remarks>
/// This is the answer to "how do I undo a posted invoice". The invoice is immutable, so the
/// answer is never to change it: a credit note posts the opposite way and both documents stay
/// visible. The customer's balance falls because the ledger says so, not because a stored
/// figure was edited.
/// </remarks>
public sealed class SalesCreditNoteService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    IReceivablesService receivables,
    SalesCreditNotePostingRule rule,
    ILogger<SalesCreditNoteService> logger) : ISalesCreditNoteService
{
    // Matches the series the seeder already creates, and the one already present on deployed
    // databases. Renaming it to "SalesCreditNote" for symmetry would orphan those rows.
    private const string DocumentType = "CreditNote";

    public async Task<SalesCreditNoteDetail> CreateDraftAsync(
        CreateSalesCreditNoteRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new PostingValidationException(
                "A credit note must carry a reason. A reduction to what a customer owes that "
                + "nobody can explain is the first thing an auditor asks about.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("A credit note needs at least one line.");
        }

        var invoice = await db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == request.SalesInvoiceId, ct)
            ?? throw new NotFoundException($"No sales invoice with id {request.SalesInvoiceId}.");

        if (invoice.LegalEntityId != request.LegalEntityId)
        {
            throw new PostingValidationException(
                "That invoice belongs to a different entity.");
        }

        if (invoice.State != DocumentState.Posted)
        {
            throw new PostingValidationException(
                "That invoice is still a draft, so there is nothing to credit. Delete or "
                + "amend the draft instead.");
        }

        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var id in accountIds)
        {
            if (!accounts.TryGetValue(id, out var account))
            {
                throw new NotFoundException($"No account with id {id}.");
            }

            if (!account.IsPostable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a heading and cannot be credited to.");
            }
        }

        // The tax rate comes from the code as at the INVOICE's date, not the credit note's.
        // The tax being reversed is the tax that invoice charged, and a regime may have
        // changed in between -- crediting at today's rate would leave the tax account holding
        // a difference that no return explains.
        var taxCodes = await ResolveTaxCodesAsync(request.Lines, invoice.DocDate, ct);

        var note = new SalesCreditNote
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId,
            LegalEntityId = invoice.LegalEntityId,
            DocDate = request.DocDate,
            SalesInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            // Both copied from the invoice: a credit reverses part of what the invoice
            // recorded, so it must do so in the same currency at the same rate.
            CurrencyCode = invoice.CurrencyCode,
            FxRate = invoice.FxRate,
            ReasonCode = request.ReasonCode.Trim(),
            Memo = request.Memo,
            State = DocumentState.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
        };

        var lineNo = 1;
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.UnitPrice <= 0)
            {
                throw new PostingValidationException(
                    $"Line {lineNo} ({line.Description}) has quantity {line.Quantity} at "
                    + $"{line.UnitPrice}. Both must be positive — the credit's direction comes "
                    + "from it being a credit note, not from a negative amount.");
            }

            note.Lines.Add(new SalesCreditNoteLine
            {
                Id = Guid.NewGuid(),
                TenantId = invoice.TenantId,
                SalesCreditNoteId = note.Id,
                LineNo = lineNo++,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                RevenueAccountId = line.AccountId,
                ProjectId = line.ProjectId,
                TaxCodeId = line.TaxCodeId,
                TaxRate = line.TaxCodeId is null ? 0m : taxCodes[line.TaxCodeId.Value].Rate,
            });
        }

        await AssertWithinOutstandingAsync(invoice, note.TotalWithTax, ct);

        db.SalesCreditNotes.Add(note);
        await db.SaveChangesAsync(ct);

        return await GetAsync(note.Id, ct);
    }

    public async Task<SalesCreditNoteDetail> PostAsync(Guid noteId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var note = await db.SalesCreditNotes
            .Include(n => n.Lines)
            .Include(n => n.Customer)
            .Include(n => n.SalesInvoice)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct)
            ?? throw new NotFoundException($"No credit note with id {noteId}.");

        if (note.State == DocumentState.Posted)
        {
            throw new PostingValidationException(
                $"Credit note {note.DocNo} is already posted.");
        }

        // Re-checked at posting, not only at draft time. Two drafts can each be within the
        // outstanding amount on their own and exceed it together, and the draft that posts
        // second is the one that has to be refused.
        await AssertWithinOutstandingAsync(note.SalesInvoice!, note.TotalWithTax, ct);

        var receivablesAccount = await db.Accounts
            .Where(a => a.TenantId == note.TenantId
                        && a.ControlType == ControlType.AccountsReceivable
                        && a.IsPostable && a.IsActive)
            .OrderBy(a => a.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "The chart of accounts has no active receivables control account.");

        var outputTax = await ResolveOutputTaxAccountsAsync(note, ct);

        var lines = rule.Build(note, new PostingRuleContext(receivablesAccount.Id, outputTax));

        var docNo = await numbers.AllocateAsync(
            note.LegalEntityId, DocumentType, note.DocDate, ct);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                note.LegalEntityId,
                note.DocDate,
                lines,
                Memo: $"Credit note {docNo} to {note.Customer!.Name}: {note.ReasonCode}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: note.Id),
            ct);

        note.DocNo = docNo;
        note.State = DocumentState.Posted;
        note.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Posted credit note {DocNo} against {Invoice} as entry {EntryNo}",
            docNo, note.SalesInvoice!.DocNo, entry.EntryNo);

        return await GetAsync(note.Id, ct);
    }

    public async Task<SalesCreditNoteDetail> GetAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await db.SalesCreditNotes
            .AsNoTracking()
            .Include(n => n.Lines).ThenInclude(l => l.RevenueAccount)
            .Include(n => n.Lines).ThenInclude(l => l.TaxCode)
            .Include(n => n.Customer)
            .Include(n => n.SalesInvoice)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct)
            ?? throw new NotFoundException($"No credit note with id {noteId}.");

        return new SalesCreditNoteDetail(
            note.Id,
            note.DocNo,
            note.DocDate,
            note.SalesInvoiceId,
            note.SalesInvoice!.DocNo,
            note.CustomerId,
            note.Customer!.Name,
            note.CurrencyCode,
            note.FxRate,
            note.ReasonCode,
            note.Memo,
            note.State.ToString(),
            note.JournalEntryId,
            note.Total,
            note.TaxTotal,
            note.TotalWithTax,
            note.Lines.OrderBy(l => l.LineNo).Select(l => new CreditNoteLineDetail(
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
                l.TaxCode is null ? null : $"{l.TaxCode.Code} — {l.TaxCode.Name}",
                l.TaxRate,
                l.TaxAmount)).ToList());
    }

    public async Task<IReadOnlyList<SalesCreditNoteSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        var notes = await db.SalesCreditNotes
            .AsNoTracking()
            .Include(n => n.Lines)
            .Include(n => n.Customer)
            .Include(n => n.SalesInvoice)
            .Where(n => n.LegalEntityId == legalEntityId)
            .OrderByDescending(n => n.DocDate)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);

        return notes
            .Select(n => new SalesCreditNoteSummary(
                n.Id,
                n.DocNo,
                n.DocDate,
                n.SalesInvoice!.DocNo,
                n.Customer!.Name,
                n.CurrencyCode,
                n.Total,
                n.TaxTotal,
                n.TotalWithTax,
                n.ReasonCode,
                n.State.ToString(),
                n.JournalEntryId))
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Refuses a credit that would take the invoice below zero.
    /// </summary>
    /// <remarks>
    /// Crediting more than is outstanding leaves the customer in credit, which is a real thing
    /// to want and a different decision from this one — it needs somewhere for the balance to
    /// sit, and until credits on account exist there is nowhere honest to put it.
    /// </remarks>
    private async Task AssertWithinOutstandingAsync(
        SalesInvoice invoice, decimal amount, CancellationToken ct)
    {
        // Only posted documents count toward what is outstanding, so the draft being checked
        // -- including the one currently being posted, which is still a draft at this point --
        // is never double counted. Two drafts can each pass on their own and the second to
        // post will be refused, which is the right moment to refuse it.
        var open = await receivables.GetOpenInvoicesAsync(invoice.LegalEntityId, invoice.CustomerId, ct);
        var outstanding = open.FirstOrDefault(i => i.Id == invoice.Id)?.Outstanding ?? 0m;

        if (amount > outstanding)
        {
            throw new PostingValidationException(
                $"Invoice {invoice.DocNo} has {outstanding:N2} {invoice.CurrencyCode} "
                + $"outstanding and this credit is for {amount:N2}. Crediting more than is "
                + "owed would leave the customer in credit, which needs a credit on account "
                + "and is not supported yet.");
        }
    }

    private async Task<Dictionary<Guid, TaxCode>> ResolveTaxCodesAsync(
        IReadOnlyList<CreateCreditNoteLine> lines, DateOnly invoiceDate, CancellationToken ct)
    {
        var ids = lines
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

            if (!code.AppliesOn(invoiceDate))
            {
                throw new PostingValidationException(
                    $"Tax code {code.Code} ({code.Name}) did not apply on "
                    + $"{invoiceDate:yyyy-MM-dd}, the date of the invoice being credited. Use a "
                    + "code that was in force when the tax was charged.");
            }
        }

        return codes;
    }

    private async Task<Dictionary<Guid, Guid>> ResolveOutputTaxAccountsAsync(
        SalesCreditNote note, CancellationToken ct)
    {
        var ids = note.Lines
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
                    $"Tax code {code.Code} has no output tax account, so the tax it charged "
                    + "cannot be credited back.");
            }

            map[code.Id] = code.OutputAccountId.Value;
        }

        return map;
    }
}
