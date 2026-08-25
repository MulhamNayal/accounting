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

/// <summary>
/// Marks an account the system itself needs to find.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ControlType"/>, which says an account's balance is composed of
/// subledger detail. This says "when the system must post somewhere specific, post here" —
/// exchange differences on settlement, the year-end transfer of profit. Without it those
/// accounts would have to be identified by code, and a chart is the customer's to renumber.
/// </remarks>
public enum AccountSystemRole
{
    None = 0,

    /// <summary>Exchange difference arising when a foreign-currency balance is settled.</summary>
    RealisedFxGainLoss = 1,

    /// <summary>Exchange difference on revaluing an open balance at period end.</summary>
    UnrealisedFxGainLoss = 2,

    /// <summary>Where the year-end close transfers accumulated profit.</summary>
    RetainedEarnings = 3,

    /// <summary>
    /// Where the residue from translating an entity into the group's presentation currency
    /// is taken.
    /// </summary>
    /// <remarks>
    /// Translating the balance sheet at closing rate and the income statement at average
    /// rate does not balance, and that is not an error — it is a real consequence of the
    /// rates differing. IAS 21 takes the difference to a separate reserve in equity rather
    /// than to profit, because it is not a gain anyone realised.
    /// </remarks>
    CurrencyTranslationReserve = 4,
}

/// <summary>Why a line appears in a consolidation.</summary>
public enum ConsolidationLineKind
{
    /// <summary>An entity's own balance, translated if necessary.</summary>
    Entity = 1,

    /// <summary>Removal of a transaction between two entities in the group.</summary>
    Elimination = 2,

    /// <summary>The residue from translating at differing rates.</summary>
    Translation = 3,
}
