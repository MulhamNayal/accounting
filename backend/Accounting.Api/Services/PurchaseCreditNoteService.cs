using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IPurchaseCreditNoteService
{
    Task<PurchaseCreditNoteDetail> CreateDraftAsync(
        CreatePurchaseCreditNoteRequest request, CancellationToken ct = default);

    Task<PurchaseCreditNoteDetail> PostAsync(Guid noteId, CancellationToken ct = default);

    Task<PurchaseCreditNoteDetail> GetAsync(Guid noteId, CancellationToken ct = default);

    Task<IReadOnlyList<PurchaseCreditNoteSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default);
}

/// <summary>
/// Credits received from suppliers, and the debit notes raised against them.
/// </summary>
/// <remarks>
/// The mirror of <see cref="SalesCreditNoteService"/>. The one addition is that the tax
/// treatment is reproduced from the bill rather than resolved afresh: if the original tax went
/// into the cost, the credit takes it back out of the cost.
/// </remarks>
public sealed class PurchaseCreditNoteService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    IPostingService postings,
    IPayablesService payables,
    PurchaseCreditNotePostingRule rule,
    ILogger<PurchaseCreditNoteService> logger) : IPurchaseCreditNoteService
{
    private const string DocumentType = "PurchaseCreditNote";

    public async Task<PurchaseCreditNoteDetail> CreateDraftAsync(
        CreatePurchaseCreditNoteRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new PostingValidationException(
                "A credit note must carry a reason. A reduction to what a supplier is owed "
                + "that nobody can explain is what an auditor asks about first.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new PostingValidationException("A credit note needs at least one line.");
        }

        var invoice = await db.PurchaseInvoices
            .Include(i => i.Lines)
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == request.PurchaseInvoiceId, ct)
            ?? throw new NotFoundException(
                $"No purchase invoice with id {request.PurchaseInvoiceId}.");

        if (invoice.LegalEntityId != request.LegalEntityId)
        {
            throw new PostingValidationException("That bill belongs to a different entity.");
        }

        if (invoice.State != DocumentState.Posted)
        {
            throw new PostingValidationException(
                "That bill is still a draft, so there is nothing to credit. Amend the draft "
                + "instead.");
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
                    $"Account {account.Code} ({account.Name}) is a heading and cannot be credited.");
            }

            if (account.ControlType is ControlType.AccountsReceivable or ControlType.AccountsPayable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a subledger control account "
                    + "and cannot be credited directly.");
            }
        }

        // Effective on the BILL's date, so the credit reverses the tax that bill actually
        // carried -- including whether it was reclaimable, which is what decides where the
        // credit goes.
        var taxCodes = await ResolveTaxCodesAsync(request.Lines, invoice.DocDate, ct);

        var note = new PurchaseCreditNote
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId,
            LegalEntityId = invoice.LegalEntityId,
            SupplierCreditNoteNo = string.IsNullOrWhiteSpace(request.SupplierCreditNoteNo)
                ? null
                : request.SupplierCreditNoteNo.Trim(),
            DocDate = request.DocDate,
            PurchaseInvoiceId = invoice.Id,
            SupplierId = invoice.SupplierId,
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
                    + $"{line.UnitPrice}. Both must be positive — the direction comes from "
                    + "this being a credit note, not from a negative amount.");
            }

            var code = line.TaxCodeId is null ? null : taxCodes[line.TaxCodeId.Value];

            note.Lines.Add(new PurchaseCreditNoteLine
            {
                Id = Guid.NewGuid(),
                TenantId = invoice.TenantId,
                PurchaseCreditNoteId = note.Id,
                LineNo = lineNo++,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                ChargeAccountId = line.AccountId,
                ProjectId = line.ProjectId,
                TaxCodeId = line.TaxCodeId,
                TaxRate = code?.Rate ?? 0m,
                TaxReclaimable = code?.TaxRegime?.InputReclaimable ?? false,
            });
        }

        await AssertWithinOutstandingAsync(invoice, note.TotalWithTax, ct);

        db.PurchaseCreditNotes.Add(note);
        await db.SaveChangesAsync(ct);

        return await GetAsync(note.Id, ct);
    }

    public async Task<PurchaseCreditNoteDetail> PostAsync(
        Guid noteId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var note = await db.PurchaseCreditNotes
            .Include(n => n.Lines)
            .Include(n => n.Supplier)
            .Include(n => n.PurchaseInvoice)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct)
            ?? throw new NotFoundException($"No credit note with id {noteId}.");

        if (note.State == DocumentState.Posted)
        {
            throw new PostingValidationException($"Credit note {note.DocNo} is already posted.");
        }

        await AssertWithinOutstandingAsync(note.PurchaseInvoice!, note.TotalWithTax, ct);

        var payablesAccount = await db.Accounts
            .Where(a => a.TenantId == note.TenantId
                        && a.ControlType == ControlType.AccountsPayable
                        && a.IsPostable && a.IsActive)
            .OrderBy(a => a.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                "The chart of accounts has no active payables control account.");

        var inputTax = await ResolveInputTaxAccountsAsync(note, ct);

        var lines = rule.Build(note, new PurchasePostingRuleContext(payablesAccount.Id, inputTax));

        var docNo = await numbers.AllocateAsync(
            note.LegalEntityId, DocumentType, note.DocDate, ct);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                note.LegalEntityId,
                note.DocDate,
                lines,
                Memo: $"Credit note {docNo} from {note.Supplier!.Name}: {note.ReasonCode}",
                SourceDocumentType: DocumentType,
                SourceDocumentId: note.Id),
            ct);

        note.DocNo = docNo;
        note.State = DocumentState.Posted;
        note.JournalEntryId = entry.Id;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Posted purchase credit note {DocNo} against {Invoice} as entry {EntryNo}",
            docNo, note.PurchaseInvoice!.SupplierInvoiceNo, entry.EntryNo);

        return await GetAsync(note.Id, ct);
    }

    public async Task<PurchaseCreditNoteDetail> GetAsync(
        Guid noteId, CancellationToken ct = default)
    {
        var note = await db.PurchaseCreditNotes
            .AsNoTracking()
            .Include(n => n.Lines).ThenInclude(l => l.ChargeAccount)
            .Include(n => n.Lines).ThenInclude(l => l.TaxCode)
            .Include(n => n.Supplier)
            .Include(n => n.PurchaseInvoice)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct)
            ?? throw new NotFoundException($"No credit note with id {noteId}.");

        return new PurchaseCreditNoteDetail(
            note.Id,
            note.DocNo,
            note.SupplierCreditNoteNo,
            note.DocDate,
            note.PurchaseInvoiceId,
            note.PurchaseInvoice!.SupplierInvoiceNo,
            note.SupplierId,
            note.Supplier!.Name,
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
                l.ChargeAccountId,
                l.ChargeAccount!.Code,
                l.ChargeAccount.Name,
                l.TaxCodeId,
                l.TaxCode is null ? null : $"{l.TaxCode.Code} — {l.TaxCode.Name}",
                l.TaxRate,
                l.TaxAmount)).ToList());
    }

    public async Task<IReadOnlyList<PurchaseCreditNoteSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        var notes = await db.PurchaseCreditNotes
            .AsNoTracking()
            .Include(n => n.Lines)
            .Include(n => n.Supplier)
            .Include(n => n.PurchaseInvoice)
            .Where(n => n.LegalEntityId == legalEntityId)
            .OrderByDescending(n => n.DocDate)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);

        return notes
            .Select(n => new PurchaseCreditNoteSummary(
                n.Id,
                n.DocNo,
                n.SupplierCreditNoteNo,
                n.DocDate,
                n.PurchaseInvoice!.SupplierInvoiceNo,
                n.Supplier!.Name,
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

    private async Task AssertWithinOutstandingAsync(
        PurchaseInvoice invoice, decimal amount, CancellationToken ct)
    {
        var open = await payables.GetOpenInvoicesAsync(
            invoice.LegalEntityId, invoice.SupplierId, ct);
        var outstanding = open.FirstOrDefault(i => i.Id == invoice.Id)?.Outstanding ?? 0m;

        if (amount > outstanding)
        {
            throw new PostingValidationException(
                $"Bill {invoice.SupplierInvoiceNo} has {outstanding:N2} {invoice.CurrencyCode} "
                + $"outstanding and this credit is for {amount:N2}. Crediting more than is owed "
                + "would leave the supplier owing us, which needs a debit balance on account "
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
                    + $"{invoiceDate:yyyy-MM-dd}, the date of the bill being credited.");
            }
        }

        return codes;
    }

    private async Task<Dictionary<Guid, Guid>> ResolveInputTaxAccountsAsync(
        PurchaseCreditNote note, CancellationToken ct)
    {
        var ids = note.Lines
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
                    $"Tax code {code.Code} has no input tax account, so reclaimable tax cannot "
                    + "be credited back.");
            }

            map[code.Id] = code.InputAccountId.Value;
        }

        return map;
    }
}
