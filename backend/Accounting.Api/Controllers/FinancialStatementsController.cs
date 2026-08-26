using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/profit-and-loss")]
public class ProfitAndLossController(IFinancialStatementsService statements) : ControllerBase
{
    /// <summary>
    /// Income and expenses for a date range, derived from postings.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProfitAndLoss>> GetAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        // Defaulting the range to the year so far matches what a person asking for "the P&L"
        // almost always means, and makes the endpoint usable without a date picker.
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? new DateOnly(end.Year, 1, 1);

        return Ok(await statements.GetProfitAndLossAsync(entityId, start, end, cancellationToken));
    }
}

[ApiController]
[Route("api/balance-sheet")]
public class BalanceSheetController(IFinancialStatementsService statements) : ControllerBase
{
    /// <summary>
    /// Assets, liabilities and equity as at a date, derived from postings.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BalanceSheet>> GetAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return Ok(await statements.GetBalanceSheetAsync(entityId, date, cancellationToken));
    }
}
