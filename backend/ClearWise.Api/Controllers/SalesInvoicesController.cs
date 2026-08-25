using ClearWise.Api.Data;
using ClearWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Controllers;

[ApiController]
[Route("api/sales-invoices")]
public class SalesInvoicesController(ISalesInvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalesInvoiceSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await invoices.ListAsync(entityId, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = "GetSalesInvoice")]
    public async Task<ActionResult<SalesInvoiceDetail>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await invoices.GetAsync(id, cancellationToken));

    /// <summary>Creates a draft. Drafts are not in the books and have no number yet.</summary>
    [HttpPost]
    public async Task<ActionResult<SalesInvoiceDetail>> CreateAsync(
        [FromBody] CreateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoices.CreateDraftAsync(request, cancellationToken);
        return CreatedAtRoute("GetSalesInvoice", new { id = invoice.Id }, invoice);
    }

    /// <summary>
    /// Posts the draft: assigns a gapless number, runs the posting rule, writes the entry.
    /// One way — there is no unpost.
    /// </summary>
    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<SalesInvoiceDetail>> PostAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await invoices.PostAsync(id, cancellationToken));
}

[ApiController]
[Route("api/customers")]
public class CustomersController(ClearWiseDbContext db) : ControllerBase
{
    /// <summary>
    /// Customers are tenant-wide, not per entity, so a group billing one client from two
    /// companies keeps a single record for them.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerSummary>>> ListAsync(
        CancellationToken cancellationToken)
        => Ok(await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CustomerSummary(
                c.Id, c.Code, c.Name, c.TaxId, c.CurrencyCode, c.CreditTermDays, c.IsActive))
            .ToListAsync(cancellationToken));
}
