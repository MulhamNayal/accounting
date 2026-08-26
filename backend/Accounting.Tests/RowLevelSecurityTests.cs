using Accounting.Api.Data;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Tests;

/// <summary>
/// Tenant isolation must be enforced by PostgreSQL, not by every query remembering a
/// predicate. These tests assert that directly.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class RowLevelSecurityTests
{
    private static async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var owner = TestDatabase.CreateOwnerContext();

        // tenants is RLS-enabled but not FORCEd, so the owner may provision across tenants.
        owner.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = $"Tenant {tenantA:N}", CreatedAtUtc = DateTimeOffset.UtcNow },
            new Tenant { Id = tenantB, Name = $"Tenant {tenantB:N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        await owner.SaveChangesAsync();

        await InsertEntityAsync(tenantA, "HOLD", "Holdings", "MYR");
        await InsertEntityAsync(tenantB, "OTHER", "Unrelated Co", "SGD");

        return (tenantA, tenantB);
    }

    private static async Task InsertEntityAsync(Guid tenantId, string code, string name, string currency)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        await using var context = TestDatabase.CreateAppContext(tenantContext);
        context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            FunctionalCurrency = currency,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_WithTenantSet_ReturnsOnlyThatTenantsRows()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA);

        await using var context = TestDatabase.CreateAppContext(tenantContext);

        // Deliberately no WHERE clause on tenant. The database must apply it.
        var entities = await context.LegalEntities.ToListAsync();

        Assert.NotEmpty(entities);
        Assert.All(entities, e => Assert.Equal(tenantA, e.TenantId));
        Assert.DoesNotContain(entities, e => e.TenantId == tenantB);
    }

    [Fact]
    public async Task Query_WithNoTenantSet_ReturnsNothing()
    {
        await SeedTwoTenantsAsync();

        // No SetTenant call: the session variable is written as an empty string.
        await using var context = TestDatabase.CreateAppContext(new TenantContext());

        var count = await context.LegalEntities.CountAsync();

        // Failing closed is the only acceptable direction. Showing too little is
        // recoverable; showing another tenant's books is not.
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Insert_ForAnotherTenant_IsRejected()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA);

        await using var context = TestDatabase.CreateAppContext(tenantContext);
        context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB, // smuggling a row into another tenant
            Code = "EVIL",
            Name = "Smuggled",
            FunctionalCurrency = "MYR",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("row-level security", exception.GetBaseException().Message);
    }

    [Fact]
    public async Task ApplicationRole_CannotUpdateOrDeletePeriodEvents()
    {
        await using var owner = TestDatabase.CreateOwnerContext();

        // period_events is the append-only record of who reopened a period and why.
        // The privilege is revoked, not merely unused - a trail the application can
        // rewrite is not a trail.
        var canUpdate = await ScalarBoolAsync(owner, "UPDATE");
        var canDelete = await ScalarBoolAsync(owner, "DELETE");
        var canInsert = await ScalarBoolAsync(owner, "INSERT");
        var canSelect = await ScalarBoolAsync(owner, "SELECT");

        Assert.False(canUpdate, "accounting_app must not hold UPDATE on period_events");
        Assert.False(canDelete, "accounting_app must not hold DELETE on period_events");
        Assert.True(canInsert, "accounting_app must be able to append period events");
        Assert.True(canSelect, "accounting_app must be able to read period events");
    }

    private static async Task<bool> ScalarBoolAsync(AccountingDbContext context, string privilege)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"SELECT has_table_privilege('accounting_app', 'period_events', '{privilege}')";
        var result = await command.ExecuteScalarAsync();
        await context.Database.CloseConnectionAsync();
        return (bool)result!;
    }
}
