namespace ClearWise.Api.Models;

/// <summary>
/// The five classifications of the accounting equation. An account's normal balance is
/// derived from this and never stored — a stored copy could disagree with the type.
/// </summary>
public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5,
}

/// <summary>
/// Marks accounts whose balance is composed of subledger detail. A posting to a control
/// account must carry the matching dimension, or the derived subledger silently loses rows.
/// </summary>
public enum ControlType
{
    None = 0,
    AccountsReceivable = 1,
    AccountsPayable = 2,
    Stock = 3,
    Tax = 4,
    Bank = 5,
}

/// <summary>
/// Posting is permitted only into <see cref="Open"/>. <see cref="SoftClosed"/> may be
/// reopened by an authorised role, and every transition is recorded.
/// <see cref="HardClosed"/> is terminal: there is deliberately no transition out of it.
/// </summary>
public enum PeriodState
{
    Open = 1,
    SoftClosed = 2,
    HardClosed = 3,
}

/// <summary>Which side of an entry a posting sits on.</summary>
public enum PostingDirection
{
    Debit = 1,
    Credit = 2,
}
