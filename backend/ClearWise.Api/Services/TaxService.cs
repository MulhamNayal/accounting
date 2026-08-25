using ClearWise.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Services;

public interface ITaxService
{
    Task<IReadOnlyList<TaxRegimeSummary>> ListRegimesAsync(CancellationToken ct = default);

    /// <summary>Codes usable on a document dated <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<TaxCodeSummary>> ListCodesAsync(DateOnly asOf, CancellationToken ct = default);
}

public sealed class TaxService(ClearWiseDbContext db) : ITaxService
{
    public async Task<IReadOnlyList<TaxRegimeSummary>> ListRegimesAsync(CancellationToken ct = default)
        => await db.TaxRegimes
            .AsNoTracking()
            .OrderBy(r => r.CountryCode).ThenBy(r => r.Code)
            .Select(r => new TaxRegimeSummary(
                r.Id, r.Code, r.Name, r.CountryCode, r.InputReclaimable,
                r.EffectiveFrom, r.EffectiveTo, r.IsActive))
            .ToListAsync(ct);

    /// <summary>
    /// Filtered by the <em>document</em> date, not today.
    /// </summary>
    /// <remarks>
    /// Back-dating a document into a period when a different regime was in force must use
    /// that regime's codes. This is how Malaysia's 2018 GST-to-SST change survives: the old
    /// codes remain selectable for old dates and disappear for new ones, and no history is
    /// restated.
    /// </remarks>
    public async Task<IReadOnlyList<TaxCodeSummary>> ListCodesAsync(
        DateOnly asOf, CancellationToken ct = default)
        => await db.TaxCodes
            .AsNoTracking()
            .Where(c => c.IsActive
                        && c.EffectiveFrom <= asOf
                        && (c.EffectiveTo == null || c.EffectiveTo >= asOf))
            .OrderBy(c => c.TaxRegime!.Code).ThenBy(c => c.Code)
            .Select(c => new TaxCodeSummary(
                c.Id,
                c.Code,
                c.Name,
                c.TaxRegime!.Code,
                c.TaxRegime.CountryCode,
                c.Kind.ToString(),
                c.Rate,
                c.OutputAccountId,
                c.InputAccountId,
                c.TaxRegime.InputReclaimable,
                c.EffectiveFrom,
                c.EffectiveTo))
            .ToListAsync(ct);
}

public record TaxRegimeSummary(
    Guid Id,
    string Code,
    string Name,
    string CountryCode,
    bool InputReclaimable,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public record TaxCodeSummary(
    Guid Id,
    string Code,
    string Name,
    string RegimeCode,
    string CountryCode,
    string Kind,
    decimal Rate,
    Guid? OutputAccountId,
    Guid? InputAccountId,
    bool InputReclaimable,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
