using Accounting.Api.Exceptions;
using Accounting.Api.Models;

namespace Accounting.Api.Services;

/// <summary>
/// Turns a sales credit note into postings: the sales invoice rule with every side swapped.
/// </summary>
/// <remarks>
/// Written as its own rule rather than by inverting the invoice rule's output. Reversing a
/// list of postings looks equivalent and is not: the credit's lines are its own, and a partial
/// credit for one line of a five-line invoice must not produce four postings of zero. The
/// duplication is a few lines and the alternative is a function whose behaviour depends on
/// which document it was handed.
/// </remarks>
public sealed class SalesCreditNotePostingRule
    : IPostingRule<SalesCreditNote, PostingRuleContext>
{
    public string DocumentType => "SalesCreditNote";

    /// <summary>
    /// Credit receivables for what the customer no longer owes, debit each line's revenue
    /// account back, and debit output tax for the tax no longer collected.
    /// </summary>
    /// <remarks>
    /// The output tax debit matters more than it looks. Tax charged on an invoice is owed to
    /// the authority; crediting the invoice without reversing the tax leaves the business
    /// paying over tax it never collected, and no report will point at why.
    /// </remarks>
    public IReadOnlyList<PostingLineRequest> Build(
        SalesCreditNote note, PostingRuleContext context)
    {
        if (note.Lines.Count == 0)
        {
            throw new PostingValidationException("A credit note needs at least one line.");
        }

        var net = note.Lines.Sum(l => l.LineTotal);
        var tax = note.Lines.Sum(l => l.TaxAmount);

        var lines = new List<PostingLineRequest>
        {
            new(
                context.ReceivablesAccountId,
                nameof(PostingDirection.Credit),
                net + tax,
                note.CurrencyCode,
                note.FxRate,
                CustomerId: note.CustomerId,
                Description: $"Credit to {note.Customer?.Name ?? "customer"}"),
        };

        foreach (var line in note.Lines.OrderBy(l => l.LineNo))
        {
            lines.Add(new PostingLineRequest(
                line.RevenueAccountId,
                nameof(PostingDirection.Debit),
                line.LineTotal,
                note.CurrencyCode,
                note.FxRate,
                ProjectId: line.ProjectId,
                AgentId: line.AgentId,
                TaxCodeId: line.TaxCodeId,
                Description: line.Description));
        }

        // Grouped by code and summed from the lines' already-rounded tax, matching the
        // invoice rule -- recomputing from the net would differ by a cent and the entry would
        // not balance.
        var byCode = note.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0)
            .GroupBy(l => l.TaxCodeId!.Value);

        foreach (var group in byCode)
        {
            if (context.OutputTaxAccountByTaxCode is null
                || !context.OutputTaxAccountByTaxCode.TryGetValue(group.Key, out var accountId))
            {
                throw new PostingValidationException(
                    "A line credits tax but its tax code has no output tax account.");
            }

            lines.Add(new PostingLineRequest(
                accountId,
                nameof(PostingDirection.Debit),
                group.Sum(l => l.TaxAmount),
                note.CurrencyCode,
                note.FxRate,
                TaxCodeId: group.Key,
                Description: "Output tax credited"));
        }

        return lines;
    }
}

/// <summary>
/// Turns a purchase credit note into postings: the purchase invoice rule with every side
/// swapped, including its treatment of irrecoverable tax.
/// </summary>
public sealed class PurchaseCreditNotePostingRule
    : IPostingRule<PurchaseCreditNote, PurchasePostingRuleContext>
{
    public string DocumentType => "PurchaseCreditNote";

    /// <summary>
    /// Debit payables for what is no longer owed, credit each line's charge account back, and
    /// credit input tax for any tax that was reclaimed.
    /// </summary>
    /// <remarks>
    /// The reclaim question is answered per line from what the bill actually did, not from the
    /// regime as it stands now. If the original tax went into the cost then the credit takes it
    /// back out of the cost, and input tax is never touched — crediting it would claim back tax
    /// that was never claimed in the first place.
    /// </remarks>
    public IReadOnlyList<PostingLineRequest> Build(
        PurchaseCreditNote note, PurchasePostingRuleContext context)
    {
        if (note.Lines.Count == 0)
        {
            throw new PostingValidationException("A credit note needs at least one line.");
        }

        var gross = note.Lines.Sum(l => l.LineTotalWithTax);

        var lines = new List<PostingLineRequest>
        {
            new(
                context.PayablesAccountId,
                nameof(PostingDirection.Debit),
                gross,
                note.CurrencyCode,
                note.FxRate,
                SupplierId: note.SupplierId,
                Description: $"Credit from {note.Supplier?.Name ?? "supplier"}"),
        };

        foreach (var line in note.Lines.OrderBy(l => l.LineNo))
        {
            lines.Add(new PostingLineRequest(
                line.ChargeAccountId,
                nameof(PostingDirection.Credit),
                line.ChargeAmount,
                note.CurrencyCode,
                note.FxRate,
                ProjectId: line.ProjectId,
                TaxCodeId: line.TaxCodeId,
                Description: line.Description));
        }

        var byCode = note.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0 && l.TaxReclaimable)
            .GroupBy(l => l.TaxCodeId!.Value);

        foreach (var group in byCode)
        {
            if (context.InputTaxAccountByTaxCode is null
                || !context.InputTaxAccountByTaxCode.TryGetValue(group.Key, out var accountId))
            {
                throw new PostingValidationException(
                    "A line credits reclaimable tax but its tax code has no input tax account.");
            }

            lines.Add(new PostingLineRequest(
                accountId,
                nameof(PostingDirection.Credit),
                group.Sum(l => l.TaxAmount),
                note.CurrencyCode,
                note.FxRate,
                TaxCodeId: group.Key,
                Description: "Input tax credited"));
        }

        return lines;
    }
}
