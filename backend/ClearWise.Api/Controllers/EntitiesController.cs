using ClearWise.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClearWise.Api.Controllers;

[ApiController]
[Route("api/entities")]
public class EntitiesController(IEntityService entities) : ControllerBase
{
    /// <summary>The legal entities in the current tenant.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LegalEntitySummary>>> GetAsync(
        CancellationToken cancellationToken)
        => Ok(await entities.ListAsync(cancellationToken));
}
