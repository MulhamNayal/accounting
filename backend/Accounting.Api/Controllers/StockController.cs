using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController(IStockService stock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ItemSummary>>> ListAsync(
        CancellationToken cancellationToken)
        => Ok(await stock.ListItemsAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ItemSummary>> CreateAsync(
        [FromBody] CreateItemRequest request, CancellationToken cancellationToken)
        => Ok(await stock.CreateItemAsync(request, cancellationToken));
}

[ApiController]
[Route("api/stock")]
public class StockController(IStockService stock) : ControllerBase
{
    /// <summary>Brings stock in at a known cost, creating the layer issues will consume.</summary>
    [HttpPost("receive")]
    public async Task<ActionResult<StockMoveSummary>> ReceiveAsync(
        [FromBody] ReceiveStockRequest request, CancellationToken cancellationToken)
        => Ok(await stock.ReceiveAsync(request, cancellationToken));

    /// <summary>
    /// Takes stock out, costed from the oldest layers with quantity remaining. The response
    /// names which layers the cost came from.
    /// </summary>
    [HttpPost("issue")]
    public async Task<ActionResult<StockIssueResult>> IssueAsync(
        [FromBody] IssueStockRequest request, CancellationToken cancellationToken)
        => Ok(await stock.IssueAsync(request, cancellationToken));

    /// <summary>
    /// Corrects the cost of a receipt already made. Never an edit â€” the difference is split
    /// between stock still held and stock already sold, and posted into the current period.
    /// </summary>
    [HttpPost("adjust-cost")]
    public async Task<ActionResult<CostAdjustmentResult>> AdjustCostAsync(
        [FromBody] AdjustCostRequest request, CancellationToken cancellationToken)
        => Ok(await stock.AdjustCostAsync(request, cancellationToken));

    /// <summary>Quantity and value on hand, both derived â€” nothing stored.</summary>
    [HttpGet("on-hand")]
    public async Task<ActionResult<IReadOnlyList<StockOnHand>>> OnHandAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await stock.GetOnHandAsync(entityId, cancellationToken));
    }

    /// <summary>The cost layers behind an item's valuation, oldest first.</summary>
    [HttpGet("layers")]
    public async Task<ActionResult<IReadOnlyList<CostLayerDetail>>> LayersAsync(
        [FromQuery] Guid entityId, [FromQuery] Guid itemId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty || itemId == Guid.Empty)
        {
            return BadRequest("entityId and itemId are required.");
        }

        return Ok(await stock.GetLayersAsync(entityId, itemId, cancellationToken));
    }

    [HttpGet("moves")]
    public async Task<ActionResult<IReadOnlyList<StockMoveSummary>>> MovesAsync(
        [FromQuery] Guid entityId, [FromQuery] Guid? itemId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await stock.GetMovesAsync(entityId, itemId, cancellationToken));
    }
}
