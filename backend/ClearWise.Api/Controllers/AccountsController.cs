using ClearWise.Api.Data;
using ClearWise.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(ClearWiseDbContext db) : ControllerBase
{
    /// <summary>The chart of accounts for the current tenant, in code order.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountSummary>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts
            .OrderBy(a => a.Code)
            .Select(a => new
            {
                a.Id,
                a.Code,
                a.Name,
                a.AccountType,
                a.ParentId,
                a.IsPostable,
                a.ControlType,
                a.IsActive,
            })
            .ToListAsync(cancellationToken);

        // NormalBalance is derived rather than stored, so it is computed here rather than
        // translated into SQL.
        var result = accounts
            .Select(a => new AccountSummary(
                a.Id,
                a.Code,
                a.Name,
                a.AccountType.ToString(),
                a.ParentId,
                a.IsPostable,
                a.ControlType.ToString(),
                NormalBalanceOf(a.AccountType).ToString(),
                a.IsActive))
            .ToList();

        return Ok(result);
    }

    private static PostingDirection NormalBalanceOf(AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => PostingDirection.Debit,
        _ => PostingDirection.Credit,
    };
}

public record AccountSummary(
    Guid Id,
    string Code,
    string Name,
    string AccountType,
    Guid? ParentId,
    bool IsPostable,
    string ControlType,
    string NormalBalance,
    bool IsActive);
