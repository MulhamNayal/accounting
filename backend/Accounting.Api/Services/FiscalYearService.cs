using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IFiscalYearService
{
    Task<IReadOnlyList<FiscalYearSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default);

    Task<FiscalYearSummary> GetAsync(Guid id, CancellationToken ct = default);

    Task<FiscalYearSummary> CreateAsync(
        CreateFiscalYearRequest request, CancellationToken ct = default);
}

/// <summary>
/// Creates financial years and the periods inside them.
/// </summary>
/// <remarks>
/// Until this existed the only code that had ever inserted a fiscal year was the development
/// seeder, so a real tenant had no periods at all and every posting failed with "no
/// accounting period covers this date". Provisioning is the first half of period close, not
/// an accessory to it.
/// </remarks>
public sealed class FiscalYearService(
    AccountingDbContext db,
    ILogger<FiscalYearService> logger) : IFiscalYearService
{
    /// <summary>
    /// A year divided into more spans than this is not a reporting calendar any tax
    /// authority recognises, and is far more likely to be a typo.
    /// </summary>
    private const int MaximumPeriodCount = 24;

    /// <summary>Newest year first.</summary>
    /// <remarks>
    /// Ordered before the projection, not after. Sorting the projected record makes EF put the
    /// whole constructed <see cref="FiscalYearSummary"/> — subqueries and all — inside the
    /// ORDER BY, which it cannot translate.
    /// </remarks>
    public async Task<IReadOnlyList<FiscalYearSummary>> ListAsync(
        Guid legalEntityId, CancellationToken ct = default) =>
        await Project(db.FiscalYears
                .Where(f => f.LegalEntityId == legalEntityId)
                .OrderByDescending(f => f.StartDate))
            .ToListAsync(ct);

    public async Task<FiscalYearSummary> GetAsync(Guid id, CancellationToken ct = default) =>
        await Project(db.FiscalYears.Where(f => f.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No fiscal year with id {id}.");

    public async Task<FiscalYearSummary> CreateAsync(
        CreateFiscalYearRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new PostingValidationException("A fiscal year needs a code, such as FY2027.");
        }

        var entity = await db.LegalEntities
            .FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        if (request.EndDate <= request.StartDate)
        {
            throw new PostingValidationException(
                $"The year ends ({request.EndDate:yyyy-MM-dd}) on or before it starts "
                + $"({request.StartDate:yyyy-MM-dd}).");
        }

        var code = request.Code.Trim();

        if (await db.FiscalYears.AnyAsync(
                f => f.LegalEntityId == entity.Id && f.Code == code, ct))
        {
            throw new PostingValidationException(
                $"{entity.Code} already has a fiscal year coded {code}.");
        }

        // Overlapping years would make the period covering a date ambiguous, and the posting
        // path resolves a period by date alone.
        var overlapping = await db.FiscalYears
            .Where(f => f.LegalEntityId == entity.Id
                        && f.StartDate <= request.EndDate
                        && f.EndDate >= request.StartDate)
            .Select(f => f.Code)
            .FirstOrDefaultAsync(ct);

        if (overlapping is not null)
        {
            throw new PostingValidationException(
                $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd} overlaps "
                + $"fiscal year {overlapping}. A date must belong to exactly one period.");
        }

        var spans = BuildPeriodSpans(request.StartDate, request.EndDate, request.PeriodCount);

        var fiscalYearId = Guid.NewGuid();

        db.FiscalYears.Add(new FiscalYear
        {
            Id = fiscalYearId,
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            Code = code,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            State = PeriodState.Open,
        });

        var sequence = 1;
        foreach (var (start, end) in spans)
        {
            db.Periods.Add(new AccountingPeriod
            {
                Id = Guid.NewGuid(),
                TenantId = entity.TenantId,
                LegalEntityId = entity.Id,
                FiscalYearId = fiscalYearId,
                Sequence = sequence++,
                StartDate = start,
                EndDate = end,
                State = PeriodState.Open,
            });
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created fiscal year {Code} for {Entity} with {Periods} periods",
            code, entity.Code, spans.Count);

        return await GetAsync(fiscalYearId, ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Divides a year into posting periods.
    /// </summary>
    /// <remarks>
    /// With no count given the periods are calendar months, which is what a normal year
    /// wants and which handles a year starting mid-month by making the first period short.
    /// With a count the range is divided into equal spans by day, the remainder going to the
    /// earlier periods — that is how a 52/53-week year of thirteen four-week periods is
    /// expressed, and it is the only sensible reading of "divide this into thirteen".
    /// </remarks>
    private static List<(DateOnly Start, DateOnly End)> BuildPeriodSpans(
        DateOnly start, DateOnly end, int? count)
    {
        var spans = new List<(DateOnly Start, DateOnly End)>();

        if (count is null)
        {
            var cursor = start;
            while (cursor <= end)
            {
                var monthEnd = new DateOnly(
                    cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                var periodEnd = monthEnd > end ? end : monthEnd;
                spans.Add((cursor, periodEnd));
                cursor = periodEnd.AddDays(1);
            }

            return spans;
        }

        var totalDays = end.DayNumber - start.DayNumber + 1;

        if (count < 1 || count > MaximumPeriodCount)
        {
            throw new PostingValidationException(
                $"A year can be divided into 1 to {MaximumPeriodCount} periods, not {count}.");
        }

        if (count > totalDays)
        {
            throw new PostingValidationException(
                $"{totalDays} days cannot be divided into {count} periods — each period needs "
                + "at least a day.");
        }

        var baseDays = totalDays / count.Value;
        var remainder = totalDays % count.Value;

        var from = start;
        for (var i = 0; i < count.Value; i++)
        {
            var days = baseDays + (i < remainder ? 1 : 0);
            var to = from.AddDays(days - 1);
            spans.Add((from, to));
            from = to.AddDays(1);
        }

        return spans;
    }

    /// <summary>
    /// A year with its period counts and the state of its close.
    /// </summary>
    /// <remarks>
    /// The closing entry is found by the backwards link on the entry rather than by anything
    /// stored on the year, so there is no second copy of "is this year closed" to disagree
    /// with the ledger. A reversed closing entry reads as no closing entry, which is what
    /// reversing one means.
    /// </remarks>
    private IQueryable<FiscalYearSummary> Project(IQueryable<FiscalYear> years) =>
        years
            .AsNoTracking()
            .Select(f => new
            {
                Year = f,
                PeriodCount = f.Periods.Count,
                OpenPeriodCount = f.Periods.Count(p => p.State == PeriodState.Open),
                Closing = db.JournalEntries
                    .Where(e => e.ClosesFiscalYearId == f.Id && e.ReversesEntryId == null)
                    .OrderByDescending(e => e.PostedAtUtc)
                    .Select(e => new
                    {
                        e.Id,
                        e.EntryNo,
                        IsReversed = db.JournalEntries.Any(r => r.ReversesEntryId == e.Id),
                    })
                    .FirstOrDefault(),
            })
            .Select(x => new FiscalYearSummary(
                x.Year.Id,
                x.Year.LegalEntityId,
                x.Year.Code,
                x.Year.StartDate,
                x.Year.EndDate,
                x.Year.State.ToString(),
                x.PeriodCount,
                x.OpenPeriodCount,
                x.Closing == null ? (Guid?)null : x.Closing.Id,
                x.Closing == null ? null : x.Closing.EntryNo,
                x.Closing != null && x.Closing.IsReversed));
}
