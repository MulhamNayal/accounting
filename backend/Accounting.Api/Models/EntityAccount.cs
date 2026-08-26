namespace Accounting.Api.Models;

/// <summary>
/// Activates a shared chart account for one entity, optionally under a local label.
/// Holdings and Realty need different accounts without needing different charts.
/// </summary>
public class EntityAccount
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Entity-specific label. Falls back to <see cref="Account.Name"/>.</summary>
    public string? LocalName { get; set; }
}
