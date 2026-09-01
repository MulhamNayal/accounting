using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Api.Services;

public interface IPeriodService
{
    Task<IReadOnlyList<PeriodSummary>> ListAsync(
        Guid legalEntityId, Guid? fiscalYearId, CancellationToken ct = default);

    Task<PeriodReadiness> GetReadinessAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<PeriodEventSummary>> GetEventsAsync(
        Guid legalEntityId, Guid? fiscalYearId, CancellationToken ct = default);

    Task<PeriodSummary> SoftCloseAsync(
        Guid periodId, string reason, CancellationToken ct = default);

    Task<PeriodSummary> ReopenAsync(
        Guid periodId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Opens and closes posting periods, recording every transition.
/// </summary>
/// <remarks>
/// The state change and its <see cref="PeriodEvent"/> are written in one transaction because
/// the database now requires it: a deferred constraint trigger refuses any change to
/// <c>periods.state</c> whose latest recorded event does not describe that exact transition.
/// The trail is the feature, so it is not left to this service to remember.
/// <para>
/// Closing runs in sequence — only the earliest open period may be closed — but reopening
/// does not. Reopening an earlier period while later ones stay closed is safe here in a way
/// it is not in most systems, because every balance is derived from postings: there is no
/// stored opening balance for the reopened month to invalidate and no recompute to trigger.
/// </para>
/// </remarks>
public sealed class PeriodService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    ILogger<PeriodService> logger) : IPeriodService
{
    public async Task<IReadOnlyList<PeriodSummary>> ListAsync(
        Guid legalEntityId, Guid? fiscalYearId, CancellationToken ct = default)
    {
        var query = db.Periods.AsNoTracking().Where(p => p.LegalEntityId == legalEntityId);

        if (fiscalYearId is not null)
        {
            query = query.Where(p => p.FiscalYearId == fiscalYearId);
        }

        return await query
            .OrderBy(p => p.StartDate)
            .Select(p => new PeriodSummary(
                p.Id,
                p.FiscalYearId,
                p.FiscalYear!.Code,
                p.Sequence,
                p.StartDate,
                p.EndDate,
                p.State.ToString(),
                db.JournalEntries.Count(e => e.PeriodId == p.Id)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PeriodEventSummary>> GetEventsAsync(
        Guid legalEntityId, Guid? fiscalYearId, CancellationToken ct = default)
    {
        var query = db.PeriodEvents
            .AsNoTracking()
            .Where(e => e.Period!.LegalEntityId == legalEntityId);

        if (fiscalYearId is not null)
        {
            query = query.Where(e => e.Period!.FiscalYearId == fiscalYearId);
        }

        return await query
            .OrderByDescending(e => e.AtUtc)
            .Select(e => new PeriodEventSummary(
                e.Id,
                e.PeriodId,
                e.Period!.Sequence,
                e.FromState.ToString(),
                e.ToState.ToString(),
                e.AtUtc,
                e.ByUser!.DisplayName,
                e.Reason))
            .ToListAsync(ct);
    }

    public async Task<PeriodReadiness> GetReadinessAsync(
        Guid periodId, CancellationToken ct = default)
    {
        var period = await db.Periods
            .AsNoTracking()
            .Include(p => p.FiscalYear)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException($"No accounting period with id {periodId}.");

        var blockers = new List<string>();

        if (period.State == PeriodState.HardClosed)
        {
            blockers.Add("The period is hard closed. There is no transition out of it.");
        }
        else if (period.State == PeriodState.SoftClosed)
        {
            blockers.Add("The period is already closed.");
        }

        if (period.FiscalYear!.State == PeriodState.HardClosed)
        {
            blockers.Add($"Fiscal year {period.FiscalYear.Code} is hard closed.");
        }

        var earlierOpen = await EarliestOpenBeforeAsync(period, ct);
        if (earlierOpen is not null)
        {
            blockers.Add(
                $"Period {earlierOpen.Sequence} ({earlierOpen.StartDate:yyyy-MM-dd} to "
                + $"{earlierOpen.EndDate:yyyy-MM-dd}) is still open, and periods close in "
                + "order.");
        }

        return new PeriodReadiness(
            period.Id,
            period.Sequence,
            period.StartDate,
            period.EndDate,
            period.State.ToString(),
            await db.JournalEntries.CountAsync(e => e.PeriodId == period.Id, ct),
            blockers,
            await CountDraftsAsync(period, ct));
    }

    public Task<PeriodSummary> SoftCloseAsync(
        Guid periodId, string reason, CancellationToken ct = default) =>
        TransitionAsync(periodId, PeriodState.Open, PeriodState.SoftClosed, reason, ct);

    public Task<PeriodSummary> ReopenAsync(
        Guid periodId, string reason, CancellationToken ct = default) =>
        TransitionAsync(periodId, PeriodState.SoftClosed, PeriodState.Open, reason, ct);

    // ---------------------------------------------------------------- helpers

    private async Task<PeriodSummary> TransitionAsync(
        Guid periodId, PeriodState from, PeriodState to, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PostingValidationException(
                "A period transition must carry a reason. A period that was closed or "
                + "reopened with nothing said about why is exactly what an auditor asks about.");
        }

        var userId = currentUser.UserId
            ?? throw new PostingValidationException(
                "No acting user. A transition that cannot be attributed to someone must not "
                + "be recorded.");

        var period = await db.Periods
            .Include(p => p.FiscalYear)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException($"No accounting period with id {periodId}.");

        if (period.State == PeriodState.HardClosed)
        {
            throw new PostingValidationException(
                $"Period {period.Sequence} of {period.FiscalYear!.Code} is hard closed. There "
                + "is no transition out of it — the year is filed.");
        }

        if (period.State != from)
        {
            throw new PostingValidationException(
                to == PeriodState.SoftClosed
                    ? $"Period {period.Sequence} of {period.FiscalYear!.Code} is already closed."
                    : $"Period {period.Sequence} of {period.FiscalYear!.Code} is already open.");
        }

        if (period.FiscalYear!.State == PeriodState.HardClosed)
        {
            throw new PostingValidationException(
                $"Fiscal year {period.FiscalYear.Code} is hard closed.");
        }

        // Sequential on the way in only. See the remarks on this class for why coming back
        // out does not need the same discipline.
        if (to == PeriodState.SoftClosed)
        {
            var earlierOpen = await EarliestOpenBeforeAsync(period, ct);
            if (earlierOpen is not null)
            {
                throw new PostingValidationException(
                    $"Period {earlierOpen.Sequence} ({earlierOpen.StartDate:yyyy-MM-dd} to "
                    + $"{earlierOpen.EndDate:yyyy-MM-dd}) is still open. Periods close in "
                    + "order, so a later month cannot be closed over an earlier one that is "
                    + "still accepting postings.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.PeriodEvents.Add(new PeriodEvent
        {
            Id = Guid.NewGuid(),
            TenantId = period.TenantId,
            PeriodId = period.Id,
            FromState = period.State,
            ToState = to,
            AtUtc = DateTimeOffset.UtcNow,
            ByUserId = userId,
            Reason = reason.Trim(),
        });

        period.State = to;

        await SaveAndCommitAsync(transaction, ct);

        logger.LogInformation(
            "Period {Sequence} of {Year} moved from {From} to {To}",
            period.Sequence, period.FiscalYear.Code, from, to);

        var summaries = await ListAsync(period.LegalEntityId, period.FiscalYearId, ct);
        return summaries.First(p => p.Id == period.Id);
    }

    /// <summary>
    /// The earliest period of the same entity that starts before this one and is still open.
    /// </summary>
    /// <remarks>
    /// Ordered by date across every fiscal year rather than by sequence within one, so the
    /// rule still holds at a year boundary — December of last year being open should block
    /// January of this one.
    /// </remarks>
    private async Task<AccountingPeriod?> EarliestOpenBeforeAsync(
        AccountingPeriod period, CancellationToken ct) =>
        await db.Periods
            .AsNoTracking()
            .Where(p => p.LegalEntityId == period.LegalEntityId
                        && p.StartDate < period.StartDate
                        && p.State == PeriodState.Open)
            .OrderBy(p => p.StartDate)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Draft documents dated inside the period, by type.
    /// </summary>
    /// <remarks>
    /// A draft is not in the books, so it does not block the close. But posting is refused
    /// into a closed period, so once the close happens these can never be posted — which is
    /// worth knowing before rather than after.
    /// </remarks>
    private async Task<List<DraftDocumentCount>> CountDraftsAsync(
        AccountingPeriod period, CancellationToken ct)
    {
        var entityId = period.LegalEntityId;
        var from = period.StartDate;
        var to = period.EndDate;

        var counts = new List<DraftDocumentCount>
        {
            new("Sales invoices", await db.SalesInvoices.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.DocDate >= from && x.DocDate <= to, ct)),
            new("Receipts", await db.CustomerReceipts.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.ReceiptDate >= from && x.ReceiptDate <= to, ct)),
            new("Bills", await db.PurchaseInvoices.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.DocDate >= from && x.DocDate <= to, ct)),
            new("Supplier payments", await db.SupplierPayments.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.PaymentDate >= from && x.PaymentDate <= to, ct)),
            new("Sales credit notes", await db.SalesCreditNotes.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.DocDate >= from && x.DocDate <= to, ct)),
            new("Purchase credit notes", await db.PurchaseCreditNotes.CountAsync(
                x => x.LegalEntityId == entityId && x.State == DocumentState.Draft
                     && x.DocDate >= from && x.DocDate <= to, ct)),
        };

        return counts.Where(c => c.Count > 0).ToList();
    }

    /// <summary>
    /// Writes the transition and commits, translating a database refusal into something
    /// readable.
    /// </summary>
    /// <remarks>
    /// The commit is inside the try because the trigger enforcing that a transition was
    /// recorded is deferred to commit, exactly like the ledger's balance check. Wrapping only
    /// the save would let that failure escape as an unhandled 500.
    /// </remarks>
    private async Task SaveAndCommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException pg)
        {
            logger.LogWarning(ex, "The database refused a period transition: {Message}", pg.MessageText);
            throw new LedgerIntegrityException(
                $"The ledger refused this period change: {pg.MessageText}", ex);
        }
    }
}
