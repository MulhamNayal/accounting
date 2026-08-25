using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Services;

public interface INumberSeriesService
{
    /// <summary>
    /// Takes the next number for a document type.
    /// </summary>
    /// <remarks>
    /// <b>Must be called inside the transaction that persists the document.</b> The counter
    /// row stays locked until that transaction ends, which is exactly what makes a gapless
    /// series gapless: if the document is not written, the increment is not written either.
    /// Allocating in its own transaction would hand back a number and then leak it.
    /// </remarks>
    Task<string> AllocateAsync(
        Guid legalEntityId, string documentType, DateOnly documentDate, CancellationToken ct = default);
}

public sealed class NumberSeriesService(ClearWiseDbContext db) : INumberSeriesService
{
    public async Task<string> AllocateAsync(
        Guid legalEntityId, string documentType, DateOnly documentDate, CancellationToken ct = default)
    {
        var series = await db.NumberSeries
            .Where(s => s.LegalEntityId == legalEntityId
                        && s.DocumentType == documentType
                        && s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                $"No active number series for {documentType} in this entity. "
                + "Create one before posting.");

        var periodKey = series.ResetPolicy switch
        {
            NumberResetPolicy.Yearly => documentDate.Year.ToString(),
            _ => string.Empty,
        };

        var next = await TakeNextAsync(series, periodKey, ct);

        return string.Format(series.Format, next, documentDate.ToDateTime(TimeOnly.MinValue));
    }

    /// <summary>
    /// Locks the counter row, reads it, and increments — all inside the caller's transaction.
    /// </summary>
    private async Task<long> TakeNextAsync(NumberSeries series, string periodKey, CancellationToken ct)
    {
        // SELECT ... FOR UPDATE serialises concurrent allocations on this series. A second
        // caller waits here rather than reading a stale value, which is the whole mechanism
        // by which two documents cannot take the same number.
        var counter = await db.NumberCounters
            .FromSql(
                $"""
                 SELECT * FROM number_counters
                 WHERE number_series_id = {series.Id} AND period_key = {periodKey}
                 FOR UPDATE
                 """)
            .FirstOrDefaultAsync(ct);

        if (counter is null)
        {
            // First document in this window. A unique index on (series, period_key) means a
            // concurrent creator loses here rather than producing a duplicate counter.
            counter = new NumberCounter
            {
                Id = Guid.NewGuid(),
                TenantId = series.TenantId,
                NumberSeriesId = series.Id,
                PeriodKey = periodKey,
                NextNumber = 1,
            };
            db.NumberCounters.Add(counter);
        }

        var allocated = counter.NextNumber;
        counter.NextNumber = allocated + 1;

        return allocated;
    }
}
