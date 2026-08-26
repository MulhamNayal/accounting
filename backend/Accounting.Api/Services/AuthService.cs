using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Accounting.Api.Auth;
using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Accounting.Api.Services;

public interface IAuthService
{
    Task<SignInResponse> SignInAsync(SignInRequest request, CancellationToken ct = default);

    Task<WhoAmIResponse> WhoAmIAsync(CancellationToken ct = default);
}

/// <summary>
/// Local sign-in and token issuance.
/// </summary>
/// <remarks>
/// The tenant travels in a signed claim, never in client-supplied input. That is the whole
/// point of this layer: row level security enforces isolation given a tenant, and until now
/// the tenant came from a request header that any caller could set.
/// </remarks>
public sealed class AuthService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _jwt = options.Value;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public async Task<SignInResponse> SignInAsync(
        SignInRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AuthenticationFailedException("Email and password are required.");
        }

        var email = request.Email.Trim().ToLowerInvariant();

        // Sign-in is the one operation that cannot already know its tenant â€” that is what it
        // is establishing. Row level security would therefore hide every user row, so the
        // lookup goes through a SECURITY DEFINER function that returns one account by exact
        // email and nothing else. A deliberate, single-purpose bypass rather than granting
        // the application any broader reach.
        var user = await FindLoginAsync(email, ct);

        // The same message and the same work either way. Saying "no such user" tells an
        // attacker which addresses are worth guessing passwords for, and returning early
        // makes the difference measurable even if the message does not.
        var verified = user is not null
            && user.IsActive
            && user.PasswordHash is not null
            && _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
                is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

        if (!verified)
        {
            _hasher.HashPassword(new AppUser { Email = email, DisplayName = email }, request.Password);
            logger.LogInformation("Failed sign-in attempt for {Email}", email);
            throw new AuthenticationFailedException("That email and password do not match.");
        }

        logger.LogInformation("Signed in {UserId} for tenant {TenantId}", user!.Id, user.TenantId);

        var expires = DateTimeOffset.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);

        return new SignInResponse(
            IssueToken(user, expires),
            expires,
            user.Id,
            user.TenantId,
            user.Email,
            user.DisplayName);
    }

    public async Task<WhoAmIResponse> WhoAmIAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new AuthenticationFailedException("Not signed in.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("The signed-in user no longer exists.");

        return new WhoAmIResponse(
            user.Id, tenantContext.TenantId ?? user.TenantId, user.Email, user.DisplayName);
    }

    /// <summary>
    /// Resolves one account by exact email, through the narrow bypass described above.
    /// </summary>
    private async Task<AppUser?> FindLoginAsync(string email, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT * FROM clearwise_resolve_login($1)";

            var parameter = command.CreateParameter();
            parameter.Value = email;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            return new AppUser
            {
                Id = reader.GetGuid(0),
                TenantId = reader.GetGuid(1),
                Email = reader.GetString(2),
                DisplayName = reader.GetString(3),
                PasswordHash = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsActive = reader.GetBoolean(5),
                SecurityStamp = reader.GetInt32(6),
            };
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private string IssueToken(AppUser user, DateTimeOffset expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(AccountingClaims.TenantId, user.TenantId.ToString()),
                new Claim(AccountingClaims.SecurityStamp, user.SecurityStamp.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName),
            ],
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Hashes a password for storage. Exposed so seeding uses the same algorithm.</summary>
    public static string HashPassword(AppUser user, string password)
        => new PasswordHasher<AppUser>().HashPassword(user, password);
}

/// <summary>Claim names that are not constants in the framework.</summary>
internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
}

public record SignInRequest(string Email, string Password);

public record SignInResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid UserId,
    Guid TenantId,
    string Email,
    string DisplayName);

public record WhoAmIResponse(Guid UserId, Guid TenantId, string Email, string DisplayName);
