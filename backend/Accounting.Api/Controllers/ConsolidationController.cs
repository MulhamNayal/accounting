using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/consolidation")]
public class ConsolidationController(IConsolidationService consolidation) : ControllerBase
{
    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<ConsolidationRunSummary>>> ListAsync(
        CancellationToken cancellationToken)
        => Ok(await consolidation.ListAsync(cancellationToken));

    [HttpGet("runs/{id:guid}", Name = "GetConsolidationRun")]
    public async Task<ActionResult<ConsolidationResult>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await consolidation.GetAsync(id, cancellationToken));

    /// <summary>
    /// Produces a consolidation and keeps it.
    /// </summary>
    /// <remarks>
    /// Stored rather than recomputed on demand, so a published consolidated statement stays
    /// reproducible — recomputing later would pick up rates and eliminations as they stand
    /// then, and the figures somebody signed would no longer be the figures reported.
    /// </remarks>
    [HttpPost("runs")]
    public async Task<ActionResult<ConsolidationResult>> RunAsync(
        [FromBody] RunConsolidationRequest request, CancellationToken cancellationToken)
    {
        var result = await consolidation.RunAsync(request, cancellationToken);
        return CreatedAtRoute("GetConsolidationRun", new { id = result.Id }, result);
    }

    /// <summary>
    /// Intercompany balances by entity pair. Each should net to nothing before consolidating.
    /// </summary>
    [HttpGet("intercompany")]
    public async Task<ActionResult<IReadOnlyList<IntercompanyPair>>> IntercompanyAsync(
        [FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
        => Ok(await consolidation.GetIntercompanyAsync(
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));
}

[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController(IExchangeRateService rates) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExchangeRateSummary>>> ListAsync(
        CancellationToken cancellationToken)
        => Ok(await rates.ListAsync(cancellationToken));

    /// <summary>
    /// Records or corrects a rate. Nothing already posted depends on it — postings store the
    /// rate they were made at — so correcting one never restates a historical figure.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ExchangeRateSummary>> UpsertAsync(
        [FromBody] UpsertExchangeRateRequest request, CancellationToken cancellationToken)
        => Ok(await rates.UpsertAsync(request, cancellationToken));
}
