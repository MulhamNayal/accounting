namespace Accounting.Api.Data;

/// <summary>
/// Carries the tenant the current request acts on. Resolved once per request and pushed
/// down to PostgreSQL as a session setting, where row level security enforces it.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    void SetTenant(Guid tenantId);
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        TenantId = tenantId;
    }
}
