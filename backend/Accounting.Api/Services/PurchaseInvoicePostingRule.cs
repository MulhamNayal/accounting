using Accounting.Api.Exceptions;
using Accounting.Api.Models;

namespace Accounting.Api.Services;

/// <summary>
/// Which accounts a purchase invoice's rule should reach for.
/// </summary>
/// <param name="PayablesAccountId">The payables control account.</param>
/// <param name="InputTaxAccountByTaxCode">
/// Where each reclaimable tax code's input tax is debited. Only codes whose regime allows a
/// reclaim appear; non-reclaimable tax never reaches an account of its own.
/// </param>
public sealed record PurchasePostingRuleContext(
    Guid PayablesAccountId,
    IReadOnlyDictionary<Guid, Guid>? InputTaxAccountByTaxCode = null);

public sealed class PurchaseInvoicePostingRule
    : IPostingRule<PurchaseInvoice, PurchasePostingRuleContext>
{
    public string DocumentType => "PurchaseInvoice";

    /// <summary>
    /// Credit payables for what the supplier is owed, debit each line's charge account for
    /// what that line cost, and debit input tax for the tax that can be reclaimed.
    /// </summary>
    /// <remarks>
    /// The mirror of the sales rule, with one real difference. Output tax is always a
    /// liability: the business collected it and owes it onward. Input tax is only an asset
    /// where the regime allows a reclaim. Where it does not, the tax is simply part of what
    /// the thing cost and belongs in the charge account — so a line's debit is its net plus
    /// its irrecoverable tax, and no separate tax posting arises.
    /// <para>
    /// Getting this wrong is not a presentational error. Treating irrecoverable tax as an
    /// asset overstates the balance sheet and understates costs, and it does so invisibly
    /// until someone tries to reclaim tax the authority never owed them.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PostingLineRequest> Build(
        PurchaseInvoice invoice, PurchasePostingRuleContext context)
    {
        if (invoice.Lines.Count == 0)
        {
            throw new PostingValidationException("A purchase invoice needs at least one line.");
        }

        var gross = invoice.Lines.Sum(l => l.LineTotalWithTax);

        if (gross <= 0)
        {
            throw new PostingValidationException(
                $"The invoice total is {gross}. A bill for nothing, or for a negative amount, "
                + "is a supplier credit note - raise one of those instead.");
        }

        var lines = new List<PostingLineRequest>
        {
            // What the supplier is owed is net plus all tax, reclaimable or not. The reclaim
            // question changes where the debit goes, never what is owed.
            new(
                context.PayablesAccountId,
                nameof(PostingDirection.Credit),
                gross,
                invoice.CurrencyCode,
                invoice.FxRate,
                SupplierId: invoice.SupplierId,
                Description: $"Invoice from {invoice.Supplier?.Name ?? "supplier"}"),
        };

        foreach (var line in invoice.Lines.OrderBy(l => l.LineNo))
        {
            if (line.LineTotal <= 0)
            {
                throw new PostingValidationException(
                    $"Line {line.LineNo} ({line.Description}) totals {line.LineTotal}. "
                    + "Every line must be positive.");
            }

            lines.Add(new PostingLineRequest(
                line.ChargeAccountId,
                nameof(PostingDirection.Debit),
                line.ChargeAmount,
                invoice.CurrencyCode,
                invoice.FxRate,
                ProjectId: line.ProjectId,
                TaxCodeId: line.TaxCodeId,
                Description: line.Description));
        }

        lines.AddRange(BuildInputTaxLines(invoice, context));

        return lines;
    }

    /// <summary>
    /// One debit per reclaimable tax code, summing the lines' already-rounded tax.
    /// </summary>
    /// <remarks>
    /// Grouped by code because a return is filed per code, and summed from the lines rather
    /// than recomputed from the net — recomputing would differ by a cent from the payables
    /// credit and the entry would not balance.
    /// </remarks>
    private static IEnumerable<PostingLineRequest> BuildInputTaxLines(
        PurchaseInvoice invoice, PurchasePostingRuleContext context)
    {
        var byCode = invoice.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0 && l.TaxReclaimable)
            .GroupBy(l => l.TaxCodeId!.Value);

        foreach (var group in byCode)
        {
            if (context.InputTaxAccountByTaxCode is null
                || !context.InputTaxAccountByTaxCode.TryGetValue(group.Key, out var accountId))
            {
                throw new PostingValidationException(
                    "A line carries reclaimable tax but its tax code has no input tax "
                    + "account. Set one on the code, or mark the regime as not reclaimable.");
            }

            yield return new PostingLineRequest(
                accountId,
                nameof(PostingDirection.Debit),
                group.Sum(l => l.TaxAmount),
                invoice.CurrencyCode,
                invoice.FxRate,
                TaxCodeId: group.Key,
                Description: "Input tax");
        }
    }
}
