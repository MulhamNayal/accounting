using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IExchangeRateService
{
    Task<ExchangeRateSummary> UpsertAsync(
        UpsertExchangeRateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ExchangeRateSummary>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// Rates used for translation and revaluation.
/// </summary>
/// <remarks>
/// Not used to value a transaction after the fact — postings store the rate they were made
/// at, so a historical figure never changes when a rate is corrected here.
/// </remarks>
public sealed class ExchangeRateService(AccountingDbContext db, ITenantContext tenantContext)
    : IExchangeRateService
{
    public async Task<ExchangeRateSummary> UpsertAsync(
        UpsertExchangeRateRequest request, CancellationToken ct = default)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new PostingValidationException("No tenant in scope.");

        var from = Normalise(request.FromCurrency, nameof(request.FromCurrency));
        var to = Normalise(request.ToCurrency, nameof(request.ToCurrency));

        if (from == to)
        {
            throw new PostingValidationException(
                "A currency's rate against itself is always one; it does not need recording.");
        }

        if (request.ClosingRate <= 0 || request.AverageRate is <= 0)
        {
            throw new PostingValidationException("Rates must be positive.");
        }

        var existing = await db.ExchangeRates.FirstOrDefaultAsync(
            r => r.FromCurrency == from && r.ToCurrency == to && r.RateDate == request.RateDate, ct);

        if (existing is null)
        {
            existing = new ExchangeRate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FromCurrency = from,
                ToCurrency = to,
                RateDate = request.RateDate,
            };
            db.ExchangeRates.Add(existing);
        }

        // Correcting a rate is legitimate: unlike a posting, this is a reference figure and
        // nothing already recorded depends on it. Consolidations that used the old value keep
        // their own stored lines, so no published figure moves.
        existing.ClosingRate = request.ClosingRate;
        existing.AverageRate = request.AverageRate;
        existing.Source = request.Source;

        await db.SaveChangesAsync(ct);

        return Summarise(existing);
    }

    public async Task<IReadOnlyList<ExchangeRateSummary>> ListAsync(CancellationToken ct = default)
        => await db.ExchangeRates
            .AsNoTracking()
            .OrderBy(r => r.FromCurrency).ThenBy(r => r.ToCurrency).ThenByDescending(r => r.RateDate)
            .Select(r => new ExchangeRateSummary(
                r.Id, r.FromCurrency, r.ToCurrency, r.RateDate,
                r.ClosingRate, r.AverageRate, r.Source))
            .ToListAsync(ct);

    private static string Normalise(string value, string field)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();

        if (code.Length != 3)
        {
            throw new PostingValidationException(
                $"{field} must be a three-letter ISO 4217 code.");
        }

        return code;
    }

    private static ExchangeRateSummary Summarise(ExchangeRate rate) => new(
        rate.Id, rate.FromCurrency, rate.ToCurrency, rate.RateDate,
        rate.ClosingRate, rate.AverageRate, rate.Source);
}
