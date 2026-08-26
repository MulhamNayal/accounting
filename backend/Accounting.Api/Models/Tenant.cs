namespace Accounting.Api.Models;

/// <summary>
/// One customer of Accounting. Every other table carries <c>TenantId</c>, and PostgreSQL
/// row level security filters on it, so isolation does not depend on every query
/// remembering a predicate.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<LegalEntity> Entities { get; set; } = [];
}
