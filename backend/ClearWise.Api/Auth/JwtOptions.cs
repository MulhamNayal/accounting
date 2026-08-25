namespace ClearWise.Api.Auth;

/// <summary>
/// Signing and lifetime settings for issued tokens.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Symmetric signing key. Must be at least 32 bytes — HMAC-SHA256 offers no more
    /// security than the key it is given, and a short key makes every token forgeable.
    /// </summary>
    /// <remarks>
    /// Never committed. Local development reads it from user-secrets; a deployment must
    /// supply it from its own secret store. Startup fails if it is missing or too short,
    /// rather than falling back to a default that would be identical on every install.
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "clearwise";

    public string Audience { get; set; } = "clearwise";

    /// <summary>
    /// Short by design. A JWT cannot be withdrawn once signed, so the window in which a
    /// stolen one is useful is bounded only by its lifetime.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 60;

    public const int MinimumKeyBytes = 32;
}

/// <summary>Claim names this application issues and reads.</summary>
public static class ClearWiseClaims
{
    /// <summary>
    /// The tenant the token is scoped to.
    /// </summary>
    /// <remarks>
    /// <b>The tenant is read from here and nowhere else.</b> It was previously taken from a
    /// request header, which meant any caller could name any tenant. A claim is signed, so
    /// it cannot be chosen by the client.
    /// </remarks>
    public const string TenantId = "cw_tenant";

    /// <summary>Matches <c>AppUser.SecurityStamp</c>; a mismatch invalidates the token.</summary>
    public const string SecurityStamp = "cw_stamp";
}
