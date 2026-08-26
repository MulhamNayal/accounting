using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IFinancialStatementsService
{
    Task<ProfitAndLoss> GetProfitAndLossAsync(
        Guid legalEntityId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<BalanceSheet> GetBalanceSheetAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// The profit and loss account and the balance sheet.
/// </summary>
/// <remarks>
/// Both are computed from postings on every request. Nothing is stored and nothing is cached,
/// so there is no figure here that can drift from the ledger it describes -- the same
/// reasoning as the trial balance, which these two agree with by construction.
/// </remarks>
public sealed class FinancialStatementsService(AccountingDbContext db) : IFinancialStatementsService
{
    public async Task<ProfitAndLoss> GetProfitAndLossAsync(
        Guid legalEntityId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new InvalidOperationException(
                $"The period ends ({to:yyyy-MM-dd}) before it starts ({from:yyyy-MM-dd}).");
        }

        var entity = await FindEntityAsync(legalEntityId, ct);

        var rows = await BalancesByAccountAsync(
            legalEntityId,
            [AccountType.Income, AccountType.Expense],
            from,
            to,
            ct);

        // Income is a credit balance and expense a debit balance, so each is signed to read
        // positive in its own section.
        var income = Section(
            "Income",
            rows.Where(r => r.AccountType == AccountType.Income)
                .Select(r => r with { Amount = r.Credit - r.Debit }));

        var expenses = Section(
            "Expenses",
            rows.Where(r => r.AccountType == AccountType.Expense)
                .Select(r => r with { Amount = r.Debit - r.Credit }));

        return new ProfitAndLoss(from, to, entity.FunctionalCurrency, income, expenses);
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default)
    {
        var entity = await FindEntityAsync(legalEntityId, ct);
        var yearStart = await ResolveFinancialYearStartAsync(entity, asOf, ct);

        var rows = await BalancesByAccountAsync(
            legalEntityId,
            [AccountType.Asset, AccountType.Liability, AccountType.Equity],
            DateOnly.MinValue,
            asOf,
            ct);

        var assets = Section(
            "Assets",
            rows.Where(r => r.AccountType == AccountType.Asset)
                .Select(r => r with { Amount = r.Debit - r.Credit }));

        var liabilities = Section(
            "Liabilities",
            rows.Where(r => r.AccountType == AccountType.Liability)
                .Select(r => r with { Amount = r.Credit - r.Debit }));

        var equity = Section(
            "Equity",
            rows.Where(r => r.AccountType == AccountType.Equity)
                .Select(r => r with { Amount = r.Credit - r.Debit }));

        // Everything earned before this financial year, and everything earned within it,
        // computed in one pass and split on the year boundary.
        var broughtForward = await ResultAsync(legalEntityId, DateOnly.MinValue, yearStart.AddDays(-1), ct);
        var thisPeriod = await ResultAsync(legalEntityId, yearStart, asOf, ct);

        return new BalanceSheet(
            asOf,
            entity.FunctionalCurrency,
            assets,
            liabilities,
            equity,
            broughtForward,
            thisPeriod);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record AccountBalance(
        Guid AccountId,
        string AccountCode,
        string AccountName,
        AccountType AccountType,
        decimal Debit,
        decimal Credit,
        decimal Amount);

    /// <summary>
    /// Debit and credit totals per account for the given types and date range.
    /// </summary>
    private async Task<List<AccountBalance>> BalancesByAccountAsync(
        Guid legalEntityId,
        AccountType[] types,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var rows = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId
                        && p.JournalEntry!.EntryDate >= from
                        && p.JournalEntry.EntryDate <= to
                        && types.Contains(p.Account!.AccountType))
            .GroupBy(p => new { p.AccountId, p.Account!.Code, p.Account.Name, p.Account.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Code,
                g.Key.Name,
                g.Key.AccountType,
                Debit = g.Where(p => p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                Credit = g.Where(p => p.Direction == PostingDirection.Credit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new AccountBalance(
                r.AccountId, r.Code, r.Name, r.AccountType, r.Debit, r.Credit, 0m))
            .ToList();
    }

    /// <summary>Income less expenses for a date range, in one query.</summary>
    private async Task<decimal> ResultAsync(
        Guid legalEntityId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from)
        {
            return 0m;
        }

        var totals = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId
                        && p.JournalEntry!.EntryDate >= from
                        && p.JournalEntry.EntryDate <= to
                        && (p.Account!.AccountType == AccountType.Income
                            || p.Account.AccountType == AccountType.Expense))
            .GroupBy(p => 1)
            .Select(g => new
            {
                IncomeCredit = g.Where(p => p.Account!.AccountType == AccountType.Income
                                            && p.Direction == PostingDirection.Credit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                IncomeDebit = g.Where(p => p.Account!.AccountType == AccountType.Income
                                           && p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                ExpenseDebit = g.Where(p => p.Account!.AccountType == AccountType.Expense
                                            && p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                ExpenseCredit = g.Where(p => p.Account!.AccountType == AccountType.Expense
                                             && p.Direction == PostingDirection.Credit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
            })
            .FirstOrDefaultAsync(ct);

        if (totals is null)
        {
            return 0m;
        }

        return (totals.IncomeCredit - totals.IncomeDebit)
               - (totals.ExpenseDebit - totals.ExpenseCredit);
    }

    /// <summary>
    /// The first day of the financial year containing <paramref name="asOf"/>.
    /// </summary>
    /// <remarks>
    /// The fiscal year table is authoritative when a row covers the date, because a first or
    /// final year can be short and only the stored row knows that. Falling back to the
    /// entity's start month means a statement can still be produced for a date outside any
    /// defined year rather than failing.
    /// </remarks>
    private async Task<DateOnly> ResolveFinancialYearStartAsync(
        LegalEntity entity, DateOnly asOf, CancellationToken ct)
    {
        var year = await db.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.LegalEntityId == entity.Id && f.StartDate <= asOf && f.EndDate >= asOf, ct);

        if (year is not null)
        {
            return year.StartDate;
        }

        var month = entity.FinancialYearStartMonth;
        return asOf.Month >= month
            ? new DateOnly(asOf.Year, month, 1)
            : new DateOnly(asOf.Year - 1, month, 1);
    }

    private async Task<LegalEntity> FindEntityAsync(Guid legalEntityId, CancellationToken ct) =>
        await db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == legalEntityId, ct)
        ?? throw new NotFoundException($"No entity with id {legalEntityId}.");

    /// <summary>
    /// Drops accounts whose balance nets to zero -- an account that saw activity which
    /// cancelled out is noise on a statement, not information.
    /// </summary>
    private static FinancialStatementSection Section(string title, IEnumerable<AccountBalance> rows)
    {
        var lines = rows
            .Where(r => r.Amount != 0m)
            .OrderBy(r => r.AccountCode)
            .Select(r => new FinancialStatementLine(r.AccountId, r.AccountCode, r.AccountName, r.Amount))
            .ToList();

        return new FinancialStatementSection(title, lines, lines.Sum(l => l.Amount));
    }
}
