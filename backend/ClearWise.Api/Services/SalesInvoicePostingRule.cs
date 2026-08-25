using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;

namespace ClearWise.Api.Services;

/// <summary>
/// Turns a document into the postings it produces.
/// </summary>
/// <remarks>
/// Rules are code, not configuration. Configurable posting rules are a large feature with
/// no customer asking for them, and a pure function from document to posting set is
/// directly testable against expected debits and credits.
/// <para>
/// A rule runs <b>once</b>, at posting time, and its output is then a frozen fact. Postings
/// are never re-derived: if a rule is later found wrong, the fix is a correcting entry for
/// the documents affected, never a recomputation that would silently restate figures
/// already reported.
/// </para>
/// </remarks>
public interface IPostingRule<in TDocument>
{
    string DocumentType { get; }

    IReadOnlyList<PostingLineRequest> Build(TDocument document, PostingRuleContext context);
}

/// <summary>
/// Which accounts a rule should reach for, resolved from the chart before it runs.
/// </summary>
/// <param name="ReceivablesAccountId">The receivables control account.</param>
/// <param name="OutputTaxAccountByTaxCode">
/// Where each tax code's output tax is credited. Resolved per code rather than globally,
/// because jurisdictions commonly separate standard-rated from other output accounts.
/// </param>
public sealed record PostingRuleContext(
    Guid ReceivablesAccountId,
    IReadOnlyDictionary<Guid, Guid>? OutputTaxAccountByTaxCode = null);

public sealed class SalesInvoicePostingRule : IPostingRule<SalesInvoice>
{
    public string DocumentType => "SalesInvoice";

    /// <summary>
    /// Debit receivables for what the customer owes, credit each line's revenue account for
    /// its net, and credit output tax for the tax charged.
    /// </summary>
    /// <remarks>
    /// The receivables line carries <c>CustomerId</c> — not optional. Receivables is a
    /// control account, so the database refuses a posting to it without a customer, and
    /// rightly: such a posting would count toward the control account while being invisible
    /// to the customer's own balance.
    /// <para>
    /// Revenue is credited per line rather than as one lump, so a project or agent
    /// dimension on a line survives into the ledger and reporting by those axes derives
    /// from the same rows as the trial balance.
    /// </para>
    /// <para>
    /// Tax is grouped by code, not by line. A return is filed per code, so the ledger should
    /// aggregate the same way — and the amount is the sum of the lines' already-rounded tax
    /// rather than a fresh calculation, or the entry would fail to balance by a cent.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PostingLineRequest> Build(SalesInvoice invoice, PostingRuleContext context)
    {
        if (invoice.Lines.Count == 0)
        {
            throw new PostingValidationException("An invoice needs at least one line.");
        }

        var net = invoice.Lines.Sum(l => l.LineTotal);

        if (net <= 0)
        {
            throw new PostingValidationException(
                $"The invoice total is {net}. An invoice for nothing, or for a negative "
                + "amount, is a credit note - raise one of those instead.");
        }

        var tax = invoice.Lines.Sum(l => l.TaxAmount);

        var lines = new List<PostingLineRequest>
        {
            // What the customer owes is net plus tax, so that is what receivables carries.
            new(
                context.ReceivablesAccountId,
                nameof(PostingDirection.Debit),
                net + tax,
                invoice.CurrencyCode,
                invoice.FxRate,
                CustomerId: invoice.CustomerId,
                Description: $"Invoice to {invoice.Customer?.Name ?? "customer"}"),
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
                line.RevenueAccountId,
                nameof(PostingDirection.Credit),
                line.LineTotal,
                invoice.CurrencyCode,
                invoice.FxRate,
                ProjectId: line.ProjectId,
                AgentId: line.AgentId,
                TaxCodeId: line.TaxCodeId,
                Description: line.Description));
        }

        lines.AddRange(BuildTaxLines(invoice, context));

        return lines;
    }

    private static IEnumerable<PostingLineRequest> BuildTaxLines(
        SalesInvoice invoice, PostingRuleContext context)
    {
        var byCode = invoice.Lines
            .Where(l => l.TaxCodeId is not null && l.TaxAmount != 0)
            .GroupBy(l => l.TaxCodeId!.Value);

        foreach (var group in byCode)
        {
            if (context.OutputTaxAccountByTaxCode is null
                || !context.OutputTaxAccountByTaxCode.TryGetValue(group.Key, out var accountId))
            {
                throw new PostingValidationException(
                    "A line charges tax but its tax code has no output tax account. "
                    + "Set one on the code before invoicing with it.");
            }

            yield return new PostingLineRequest(
                accountId,
                nameof(PostingDirection.Credit),
                group.Sum(l => l.TaxAmount),
                invoice.CurrencyCode,
                invoice.FxRate,
                TaxCodeId: group.Key,
                Description: "Output tax");
        }
    }
}
