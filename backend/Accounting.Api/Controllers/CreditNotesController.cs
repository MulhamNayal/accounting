using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/sales-credit-notes")]
public class SalesCreditNotesController(ISalesCreditNoteService notes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalesCreditNoteSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await notes.ListAsync(entityId, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = "GetSalesCreditNote")]
    public async Task<ActionResult<SalesCreditNoteDetail>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await notes.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SalesCreditNoteDetail>> CreateAsync(
        CreateSalesCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var created = await notes.CreateDraftAsync(request, cancellationToken);
        return CreatedAtRoute("GetSalesCreditNote", new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<SalesCreditNoteDetail>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await notes.PostAsync(id, cancellationToken));
}

[ApiController]
[Route("api/purchase-credit-notes")]
public class PurchaseCreditNotesController(IPurchaseCreditNoteService notes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PurchaseCreditNoteSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await notes.ListAsync(entityId, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = "GetPurchaseCreditNote")]
    public async Task<ActionResult<PurchaseCreditNoteDetail>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await notes.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PurchaseCreditNoteDetail>> CreateAsync(
        CreatePurchaseCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var created = await notes.CreateDraftAsync(request, cancellationToken);
        return CreatedAtRoute("GetPurchaseCreditNote", new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<PurchaseCreditNoteDetail>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await notes.PostAsync(id, cancellationToken));
}
