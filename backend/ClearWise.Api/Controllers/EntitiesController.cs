using ClearWise.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Controllers;

[ApiController]
[Route("api/entities")]
public class EntitiesController(ClearWiseDbContext db) : ControllerBase
{
    /// <summary>
    /// The legal entities in the current tenant.
    /// </summary>
    /// <remarks>
    /// Note the absence of a tenant predicate. Row level security applies it, so this
    /// cannot return another tenant's entities even if the filter is forgotten.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LegalEntitySummary>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var entities = await db.LegalEntities
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
            .ToListAsync(cancellationToken);

        return Ok(entities);
    }
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
