using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/tax")]
public class TaxController(ITaxService tax) : ControllerBase
{
    /// <summary>
    /// The tax systems configured for this tenant, one per jurisdiction it files in.
    /// </summary>
    [HttpGet("regimes")]
    public async Task<ActionResult<IReadOnlyList<TaxRegimeSummary>>> RegimesAsync(
        CancellationToken cancellationToken)
        => Ok(await tax.ListRegimesAsync(cancellationToken));

    /// <summary>
    /// Codes usable on a document dated <paramref name="asOf"/>, defaulting to today.
    /// </summary>
    /// <remarks>
    /// Effective-dated deliberately: back-dating a document into a period when a different
    /// regime was in force must offer that regime's codes, not today's.
    /// </remarks>
    [HttpGet("codes")]
    public async Task<ActionResult<IReadOnlyList<TaxCodeSummary>>> CodesAsync(
        [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
        => Ok(await tax.ListCodesAsync(
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));
}
