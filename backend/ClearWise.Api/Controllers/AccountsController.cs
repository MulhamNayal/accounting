using ClearWise.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClearWise.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(IChartOfAccountsService chart) : ControllerBase
{
    /// <summary>The chart of accounts for the current tenant, in code order.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountSummary>>> GetAsync(
        CancellationToken cancellationToken)
        => Ok(await chart.ListAsync(cancellationToken));
}
