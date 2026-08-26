using Accounting.Api.Data;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IChartOfAccountsService
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken ct = default);
}

public sealed class ChartOfAccountsService(AccountingDbContext db) : IChartOfAccountsService
{
    /// <summary>
    /// The tenant's chart, in code order. Returned flat with <c>ParentId</c>; the client
    /// rebuilds the tree.
    /// </summary>
    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken ct = default)
    {
        var accounts = await db.Accounts
            .AsNoTracking()
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
                a.SystemRole,
                a.IsActive,
            })
            .ToListAsync(ct);

        // NormalBalance is derived from the type rather than stored, so it is computed here
        // instead of being translated into SQL.
        return accounts
            .Select(a => new AccountSummary(
                a.Id,
                a.Code,
                a.Name,
                a.AccountType.ToString(),
                a.ParentId,
                a.IsPostable,
                a.ControlType.ToString(),
                a.SystemRole.ToString(),
                NormalBalanceOf(a.AccountType).ToString(),
                a.IsActive))
            .ToList();
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
    string SystemRole,
    string NormalBalance,
    bool IsActive);
