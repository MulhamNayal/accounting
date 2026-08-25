namespace ClearWise.Api.Models;

/// <summary>
/// An account in the chart. The chart lives at tenant level and is shared by every entity,
/// which is what makes consolidation a sum rather than a mapping exercise. Entities opt in
/// to the accounts they use via <see cref="EntityAccount"/>.
/// </summary>
public class Account
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public AccountType AccountType { get; set; }

    /// <summary>Parent for rollup. Null for a root account.</summary>
    public Guid? ParentId { get; set; }
    public Account? Parent { get; set; }
    public ICollection<Account> Children { get; set; } = [];

    /// <summary>
    /// Only leaf accounts may be posted to. Parents exist to aggregate, and posting to one
    /// makes its rollup meaningless.
    /// </summary>
    public bool IsPostable { get; set; } = true;

    /// <summary>
    /// If set, postings to this account must carry the corresponding dimension — a
    /// receivables posting must name a customer. Enforced at post time in Layer 1.
    /// </summary>
    public ControlType ControlType { get; set; } = ControlType.None;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Derived, never stored. Assets and expenses increase on the debit side; liabilities,
    /// equity and income increase on the credit side.
    /// </summary>
    public PostingDirection NormalBalance => AccountType switch
    {
        AccountType.Asset or AccountType.Expense => PostingDirection.Debit,
        _ => PostingDirection.Credit,
    };

    public ICollection<EntityAccount> EntityAccounts { get; set; } = [];
}
