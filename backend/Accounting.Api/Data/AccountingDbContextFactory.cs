using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounting.Api.Data;

/// <summary>
/// Used by <c>dotnet ef</c> at design time only.
/// </summary>
/// <remarks>
/// Migrations connect as <c>accounting_owner</c>, not as the application role. The
/// application deliberately lacks the privileges to alter schema Ã¢â‚¬â€ and from Layer 1 it
/// lacks UPDATE and DELETE on the ledger tables entirely Ã¢â‚¬â€ so it could not apply a
/// migration even if asked to. Keeping the two connection strings separate is what makes
/// that revocation meaningful rather than decorative.
/// <para>No tenant interceptor here: schema work is not tenant-scoped.</para>
/// </remarks>
public sealed class AccountingDbContextFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AccountingDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("MigrationDatabase")
            ?? throw new InvalidOperationException(
                "Connection string 'MigrationDatabase' is not configured. Set it with: "
                + "dotnet user-secrets set \"ConnectionStrings:MigrationDatabase\" \"...\"");

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AccountingDbContext(options);
    }
}
