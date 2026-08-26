namespace Accounting.Api.Services;

/// <summary>
/// One account's contribution to a statement, signed so that it reads the way an accountant
/// expects: income and liabilities positive when credit, assets and expenses positive when
/// debit. Nobody wants to read a profit and loss where revenue is negative.
/// </summary>
public record FinancialStatementLine(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal Amount);

public record FinancialStatementSection(
    string Title,
    IReadOnlyList<FinancialStatementLine> Lines,
    decimal Total);

/// <summary>
/// Income and expenses for a date range. Derived from postings on every request, like the
/// trial balance -- there is no stored figure that could disagree with the ledger.
/// </summary>
public record ProfitAndLoss(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    FinancialStatementSection Income,
    FinancialStatementSection Expenses)
{
    public decimal NetProfit => Income.Total - Expenses.Total;
}

/// <summary>
/// Position as at a date.
/// </summary>
/// <remarks>
/// <para>
/// Equity is presented in three parts: the equity accounts themselves, accumulated profit
/// from before the current financial year, and the result so far this year. That split is
/// what makes the statement legible; the accounting reason it is computed rather than read
/// from an account is that this system has no year-end close yet.
/// </para>
/// <para>
/// The arithmetic survives a close being added later. A close posts the year's income and
/// expenses into retained earnings, which zeroes those accounts for the closed year -- so
/// <see cref="RetainedEarningsBroughtForward"/>, computed from profit and loss account
/// balances, becomes zero for that year exactly as the retained earnings account picks the
/// figure up. The two can never double count.
/// </para>
/// </remarks>
public record BalanceSheet(
    DateOnly AsOf,
    string CurrencyCode,
    FinancialStatementSection Assets,
    FinancialStatementSection Liabilities,
    FinancialStatementSection Equity,
    decimal RetainedEarningsBroughtForward,
    decimal ResultForThePeriod)
{
    public decimal TotalEquity =>
        Equity.Total + RetainedEarningsBroughtForward + ResultForThePeriod;

    public decimal TotalLiabilitiesAndEquity => Liabilities.Total + TotalEquity;

    /// <summary>
    /// Assets must equal liabilities plus equity. This is not a checkbox -- if it is false,
    /// either an entry was posted unbalanced (which the database is supposed to prevent) or
    /// this calculation is wrong.
    /// </summary>
    public bool IsBalanced => Assets.Total == TotalLiabilitiesAndEquity;
}
