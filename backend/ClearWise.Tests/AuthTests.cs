using System.IdentityModel.Tokens.Jwt;
using ClearWise.Api.Auth;
using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;
using ClearWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearWise.Tests;

[Collection(nameof(DatabaseCollection))]
public class AuthTests
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly JwtOptions Options = new()
    {
        SigningKey = "test-only-signing-key-at-least-32-bytes-long!!",
        Issuer = "clearwise-test",
        Audience = "clearwise-test",
        AccessTokenMinutes = 60,
    };

    private static AuthService ServiceFor(LedgerWorld world, out ClearWiseDbContext db)
    {
        db = world.NewAppContext();
        var user = new CurrentUser();
        var tenant = new TenantContext();
        return new AuthService(
            db, user, tenant, new OptionsWrapper<JwtOptions>(Options),
            NullLogger<AuthService>.Instance);
    }

    /// <summary>Gives the fixture's user a usable local credential.</summary>
    private static async Task<string> GivePasswordAsync(
        LedgerWorld world, string password = Password, bool active = true)
    {
        await using var db = world.NewAppContext();
        var user = await db.Users.FirstAsync(u => u.Id == world.UserId);
        user.PasswordHash = AuthService.HashPassword(user, password);
        user.IsActive = active;
        await db.SaveChangesAsync();
        return user.Email;
    }

    [Fact]
    public async Task SignIn_WithCorrectCredentials_IssuesATokenScopedToTheUsersTenant()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        var response = await auth.SignInAsync(new SignInRequest(email, Password));

        Assert.Equal(world.UserId, response.UserId);
        Assert.Equal(world.TenantId, response.TenantId);
        Assert.True(response.ExpiresAtUtc > DateTimeOffset.UtcNow);

        // The tenant travels as a signed claim. This is the whole point: previously it came
        // from a request header, so any caller could name any tenant and row level security
        // would faithfully serve them that tenant's books.
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Equal(
            world.TenantId.ToString(),
            token.Claims.Single(c => c.Type == ClearWiseClaims.TenantId).Value);
        Assert.Equal(
            world.UserId.ToString(),
            token.Claims.Single(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task IssuedToken_IsSignedWithTheConfiguredKeyAndAudience()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        var response = await auth.SignInAsync(new SignInRequest(email, Password));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal(Options.Issuer, token.Issuer);
        Assert.Contains(Options.Audience, token.Audiences);
        Assert.Equal("HS256", token.SignatureAlgorithm);
    }

    [Fact]
    public async Task SignIn_WithTheWrongPassword_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => auth.SignInAsync(new SignInRequest(email, "not-the-password")));
    }

    [Fact]
    public async Task SignIn_WithAnUnknownEmail_FailsIdenticallyToAWrongPassword()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        var wrongPassword = await Record.ExceptionAsync(
            () => auth.SignInAsync(new SignInRequest(email, "wrong")));
        var unknownEmail = await Record.ExceptionAsync(
            () => auth.SignInAsync(new SignInRequest("nobody@example.test", Password)));

        // Distinguishing the two tells an attacker which addresses are worth guessing
        // passwords against, so the message must be the same.
        Assert.NotNull(wrongPassword);
        Assert.NotNull(unknownEmail);
        Assert.Equal(wrongPassword!.Message, unknownEmail!.Message);
    }

    [Fact]
    public async Task SignIn_ForAnInactiveUser_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world, active: false);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => auth.SignInAsync(new SignInRequest(email, Password)));
    }

    [Fact]
    public async Task SignIn_ForAUserWithNoLocalPassword_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();

        await using (var db = world.NewAppContext())
        {
            var user = await db.Users.FirstAsync(u => u.Id == world.UserId);
            // An external-identity user has no local credential; that must not mean "any
            // password will do".
            user.PasswordHash = null;
            await db.SaveChangesAsync();
        }

        var email = (await GetEmailAsync(world));
        var auth = ServiceFor(world, out var db2);
        await using var _ = db2;

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => auth.SignInAsync(new SignInRequest(email, "anything")));
    }

    [Fact]
    public async Task SignIn_WithMissingCredentials_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => auth.SignInAsync(new SignInRequest("", "")));
    }

    [Fact]
    public async Task SignIn_IsNotCaseSensitiveOnEmail()
    {
        var world = await LedgerFixture.CreateAsync();
        var email = await GivePasswordAsync(world);
        var auth = ServiceFor(world, out var db);
        await using var _ = db;

        var response = await auth.SignInAsync(new SignInRequest(email.ToUpperInvariant(), Password));

        Assert.Equal(world.UserId, response.UserId);
    }

    [Fact]
    public async Task StoredHash_IsNeitherThePasswordNorStableAcrossUsers()
    {
        var world = await LedgerFixture.CreateAsync();
        await GivePasswordAsync(world);

        await using var db = world.NewAppContext();
        var hash = (await db.Users.AsNoTracking().FirstAsync(u => u.Id == world.UserId)).PasswordHash!;

        Assert.DoesNotContain(Password, hash);

        // Salted, so the same password hashes differently every time. Identical hashes
        // would tell an attacker which accounts share a password.
        var other = new AppUser { Email = "other@example.test", DisplayName = "Other" };
        Assert.NotEqual(hash, AuthService.HashPassword(other, Password));
    }

    private static async Task<string> GetEmailAsync(LedgerWorld world)
    {
        await using var db = world.NewAppContext();
        return (await db.Users.AsNoTracking().FirstAsync(u => u.Id == world.UserId)).Email;
    }
}
