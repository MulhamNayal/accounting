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

/// <summary>Which accounts a rule should reach for, resolved from the chart.</summary>
public sealed record PostingRuleContext(Guid ReceivablesAccountId);

public sealed class SalesInvoicePostingRule : IPostingRule<SalesInvoice>
{
    public string DocumentType => "SalesInvoice";

    /// <summary>
    /// Debit receivables for the invoice total, credit each line's revenue account.
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
    /// </remarks>
    public IReadOnlyList<PostingLineRequest> Build(SalesInvoice invoice, PostingRuleContext context)
    {
        if (invoice.Lines.Count == 0)
        {
            throw new PostingValidationException("An invoice needs at least one line.");
        }

        var total = invoice.Lines.Sum(l => l.LineTotal);

        if (total <= 0)
        {
            throw new PostingValidationException(
                $"The invoice total is {total}. An invoice for nothing, or for a negative "
                + "amount, is a credit note - raise one of those instead.");
        }

        var lines = new List<PostingLineRequest>
        {
            new(
                context.ReceivablesAccountId,
                nameof(PostingDirection.Debit),
                total,
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
                Description: line.Description));
        }

        return lines;
    }
}
