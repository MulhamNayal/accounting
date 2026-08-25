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

    /// <summary>Subject identifier from an external identity provider, when one is used.</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>
    /// PBKDF2 hash for local sign-in. Null when the user authenticates through an external
    /// provider, which is why this is nullable rather than required — the two are
    /// alternatives, and a user with neither simply cannot sign in.
    /// </summary>
    /// <remarks>
    /// Never a password, never reversible, and never returned by any endpoint.
    /// </remarks>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Bumped to invalidate every token already issued to this user — a password change, a
    /// suspected compromise, or an administrator revoking access.
    /// </summary>
    /// <remarks>
    /// A JWT cannot be withdrawn once signed, so the only way to end a session early is for
    /// the token to carry a value the server can compare against something it controls.
    /// </remarks>
    public int SecurityStamp { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
