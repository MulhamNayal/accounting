using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(ISupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierSummary>>> GetAsync(
        CancellationToken cancellationToken)
        => Ok(await suppliers.ListAsync(cancellationToken));
}

[ApiController]
[Route("api/purchase-invoices")]
public class PurchaseInvoicesController(IPurchaseInvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PurchaseInvoiceSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await invoices.ListAsync(entityId, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = "GetPurchaseInvoice")]
    public async Task<ActionResult<PurchaseInvoiceDetail>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await invoices.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PurchaseInvoiceDetail>> CreateAsync(
        CreatePurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        var created = await invoices.CreateDraftAsync(request, cancellationToken);

        // CreatedAtRoute with an explicit route name: ASP.NET strips the "Async" suffix from
        // action names, so nameof(GetAsync) does not resolve -- and it throws after the work
        // has committed, which reads to the caller as a failure that actually succeeded.
        return CreatedAtRoute("GetPurchaseInvoice", new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<PurchaseInvoiceDetail>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await invoices.PostAsync(id, cancellationToken));
}

[ApiController]
[Route("api/payments")]
public class PaymentsController(IPayablesService payables) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await payables.ListPaymentsAsync(entityId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<PaymentSummary>> CreateAsync(
        CreatePaymentRequest request, CancellationToken cancellationToken)
        => Ok(await payables.CreatePaymentAsync(request, cancellationToken));

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<PaymentSummary>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await payables.PostPaymentAsync(id, cancellationToken));
}

[ApiController]
[Route("api/payment-allocations")]
public class PaymentAllocationsController(IPayablesService payables) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<PaymentAllocationDetail>>> AllocateAsync(
        AllocatePaymentRequest request, CancellationToken cancellationToken)
        => Ok(await payables.AllocateAsync(request, cancellationToken));

    /// <summary>
    /// Undoes an allocation by inserting a reversing row. Nothing is deleted.
    /// </summary>
    [HttpPost("{id:guid}/unallocate")]
    public async Task<ActionResult<PaymentAllocationDetail>> UnallocateAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await payables.UnallocateAsync(id, cancellationToken));
}

[ApiController]
[Route("api/payables")]
public class PayablesReportsController(IPayablesService payables) : ControllerBase
{
    [HttpGet("open-invoices")]
    public async Task<ActionResult<IReadOnlyList<OpenPurchaseInvoice>>> OpenInvoicesAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid? supplierId,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await payables.GetOpenInvoicesAsync(entityId, supplierId, cancellationToken));
    }

    [HttpGet("ageing")]
    public async Task<ActionResult<PayablesAgeingReport>> AgeingAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await payables.GetAgeingAsync(entityId, date, cancellationToken));
    }

    [HttpGet("statement")]
    public async Task<ActionResult<SupplierStatement>> StatementAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid supplierId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty || supplierId == Guid.Empty)
        {
            return BadRequest("entityId and supplierId are both required.");
        }

        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await payables.GetStatementAsync(entityId, supplierId, date, cancellationToken));
    }
}
