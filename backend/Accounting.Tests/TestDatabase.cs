using Accounting.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Tests;

/// <summary>
/// Connection details for the local test database.
/// </summary>
/// <remarks>
/// Tests run against a real PostgreSQL instance, never an in-memory provider. Row level
/// security, FORCE ROW LEVEL SECURITY and revoked privileges are precisely what is under
/// test here, and no in-memory provider implements any of them Ã¢â‚¬â€ a suite that passed
/// against one would be actively misleading.
/// <para>
/// The fallback credentials below are local-development-only role names created by
/// <c>docs/development.md</c>. Override with ACCOUNTING_TEST_DB_OWNER and
/// ACCOUNTING_TEST_DB_APP on CI.
/// </para>
/// </remarks>
public static class TestDatabase
{
    private const string DefaultOwner =
        "Host=localhost;Port=5432;Database=accounting_test;Username=accounting_owner;Password=accounting_owner";

    private const string DefaultApp =
        "Host=localhost;Port=5432;Database=accounting_test;Username=accounting_app;Password=accounting_app";

    public static string OwnerConnectionString =>
        Environment.GetEnvironmentVariable("ACCOUNTING_TEST_DB_OWNER") ?? DefaultOwner;

    public static string AppConnectionString =>
        Environment.GetEnvironmentVariable("ACCOUNTING_TEST_DB_APP") ?? DefaultApp;

    /// <summary>
    /// A context on the owner connection Ã¢â‚¬â€ schema work and arranging test data. No tenant
    /// interceptor, so the caller sets <c>app.current_tenant</c> explicitly.
    /// </summary>
    public static AccountingDbContext CreateOwnerContext() =>
        new(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>
    /// A context on the low-privilege application connection, with the tenant interceptor
    /// wired up exactly as the running application has it.
    /// </summary>
    public static AccountingDbContext CreateAppContext(ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(AppConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantConnectionInterceptor(tenantContext))
            .Options);
}

/// <summary>Migrates the test database once per test run.</summary>
public sealed class DatabaseFixture
{
    public DatabaseFixture()
    {
        using var context = TestDatabase.CreateOwnerContext();
        context.Database.Migrate();
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
