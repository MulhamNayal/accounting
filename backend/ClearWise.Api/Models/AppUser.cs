namespace ClearWise.Api.Models;

/// <summary>
/// A person who can act in a tenant. ClearWise deliberately does not own credentials —
/// <see cref="ExternalAuthId"/> points at whichever identity provider is in use, so the
/// choice can change without a data migration.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Subject identifier from the external identity provider.</summary>
    public string? ExternalAuthId { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
