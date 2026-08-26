using Accounting.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IEntityService
{
    Task<IReadOnlyList<LegalEntitySummary>> ListAsync(CancellationToken ct = default);
}

public sealed class EntityService(AccountingDbContext db) : IEntityService
{
    /// <summary>
    /// The legal entities in the current tenant.
    /// </summary>
    /// <remarks>
    /// No tenant predicate here on purpose. Row level security applies it, so this cannot
    /// return another tenant's entities even if the filter is forgotten.
    /// </remarks>
    public async Task<IReadOnlyList<LegalEntitySummary>> ListAsync(CancellationToken ct = default)
        => await db.LegalEntities
            .AsNoTracking()
            .OrderBy(e => e.Code)
            .Select(e => new LegalEntitySummary(
                e.Id,
                e.Code,
                e.Name,
                e.RegistrationNo,
                e.TaxId,
                e.FunctionalCurrency,
                e.FinancialYearStartMonth,
                e.IsActive))
            .ToListAsync(ct);
}

public record LegalEntitySummary(
    Guid Id,
    string Code,
    string Name,
    string? RegistrationNo,
    string? TaxId,
    string FunctionalCurrency,
    int FinancialYearStartMonth,
    bool IsActive);
