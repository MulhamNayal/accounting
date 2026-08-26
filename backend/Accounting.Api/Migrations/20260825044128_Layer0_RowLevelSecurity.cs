using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Enables PostgreSQL row level security so tenant isolation is enforced by the
    /// database rather than by every query remembering a predicate, and grants the
    /// application role the narrow privileges it actually needs.
    /// </summary>
    public partial class Layer0_RowLevelSecurity : Migration
    {
        /// <summary>Tables scoped by a tenant_id column.</summary>
        private static readonly string[] TenantScopedTables =
        [
            "users",
            "legal_entities",
            "accounts",
            "entity_accounts",
            "fiscal_years",
            "periods",
            "period_events",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The application role owns nothing and creates nothing. It gets exactly the
            // DML it needs, plus default privileges so tables added by later migrations
            // are covered without anyone having to remember this step.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA public TO clearwise_app;");
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO clearwise_app;");
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE clearwise_owner IN SCHEMA public "
                + "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO clearwise_app;");

            // period_events records who reopened a period and why. A trail the application
            // can rewrite is not a trail, so the privilege is removed rather than merely
            // left unused. Layer 1 applies this same mechanism to the ledger tables.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON period_events FROM clearwise_app;");

            // tenants is keyed by the tenant id itself, so its policy filters on id.
            // Deliberately ENABLE without FORCE: provisioning a tenant is inherently a
            // cross-tenant operation and the owner role performs it. Every other table is
            // FORCEd, which subjects the owner to the policy as well.
            migrationBuilder.Sql("ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON tenants "
                + "USING (id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) "
                + "WITH CHECK (id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);");

            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");

                // NULLIF maps both an unset and an empty setting to NULL, and
                // `tenant_id = NULL` is never true. With no tenant set the query returns
                // nothing, which is the correct direction to fail: showing too little is
                // recoverable, showing another tenant's books is not.
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {table} "
                    + "USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) "
                    + "WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON tenants;");
            migrationBuilder.Sql("ALTER TABLE tenants DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("GRANT UPDATE, DELETE ON period_events TO clearwise_app;");
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE clearwise_owner IN SCHEMA public "
                + "REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM clearwise_app;");
            migrationBuilder.Sql(
                "REVOKE SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public "
                + "FROM clearwise_app;");
            migrationBuilder.Sql("REVOKE USAGE ON SCHEMA public FROM clearwise_app;");
        }
    }
}
