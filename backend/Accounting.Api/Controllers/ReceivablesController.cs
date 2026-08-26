using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/receipts")]
public class ReceiptsController(IReceivablesService receivables) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReceiptSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await receivables.ListReceiptsAsync(entityId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ReceiptSummary>> CreateAsync(
        [FromBody] CreateReceiptRequest request, CancellationToken cancellationToken)
        => Ok(await receivables.CreateReceiptAsync(request, cancellationToken));

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<ReceiptSummary>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await receivables.PostReceiptAsync(id, cancellationToken));
}

[ApiController]
[Route("api/allocations")]
public class AllocationsController(IReceivablesService receivables) : ControllerBase
{
    /// <summary>Applies a posted receipt against one or more posted invoices.</summary>
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<AllocationDetail>>> AllocateAsync(
        [FromBody] AllocateRequest request, CancellationToken cancellationToken)
        => Ok(await receivables.AllocateAsync(request, cancellationToken));

    /// <summary>
    /// Undoes an allocation by inserting a reversing row. Nothing is deleted — which invoice
    /// a payment was applied to is a fact worth keeping.
    /// </summary>
    [HttpPost("{id:guid}/unallocate")]
    public async Task<ActionResult<AllocationDetail>> UnallocateAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await receivables.UnallocateAsync(id, cancellationToken));
}

[ApiController]
[Route("api/receivables")]
public class ReceivablesReportsController(IReceivablesService receivables) : ControllerBase
{
    /// <summary>Posted invoices with anything still outstanding.</summary>
    [HttpGet("open-invoices")]
    public async Task<ActionResult<IReadOnlyList<OpenInvoice>>> OpenInvoicesAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await receivables.GetOpenInvoicesAsync(entityId, customerId, cancellationToken));
    }

    /// <summary>
    /// Ageing by customer. The total equals the receivables control account balance in the
    /// trial balance, because both are the same postings summed differently.
    /// </summary>
    [HttpGet("ageing")]
    public async Task<ActionResult<AgeingReport>> AgeingAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await receivables.GetAgeingAsync(
            entityId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));
    }

    [HttpGet("statement")]
    public async Task<ActionResult<CustomerStatement>> StatementAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid customerId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty || customerId == Guid.Empty)
        {
            return BadRequest("entityId and customerId are required.");
        }

        return Ok(await receivables.GetStatementAsync(
            entityId, customerId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));
    }
}
