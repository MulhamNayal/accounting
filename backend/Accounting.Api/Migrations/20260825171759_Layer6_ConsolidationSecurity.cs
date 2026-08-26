using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for rates and consolidations, and a published consolidation frozen.
    /// </summary>
    public partial class Layer6_ConsolidationSecurity : Migration
    {
        private static readonly string[] Tables =
            ["exchange_rates", "consolidation_runs", "consolidation_postings"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {table} "
                    + "USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) "
                    + "WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);");
            }

            // A consolidation is a published figure. Recomputing or editing one later would
            // pick up rates and eliminations as they stand then, so the numbers somebody
            // signed would no longer be the numbers the system reports. Run a new one instead.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON consolidation_runs FROM clearwise_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON consolidation_postings FROM clearwise_app;");

            // Rates keep UPDATE on purpose: unlike a posting, a rate is a reference figure and
            // nothing recorded depends on it. Postings store the rate they were made at, and a
            // consolidation stores its own translated lines, so correcting a rate here cannot
            // restate any historical number.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON consolidation_postings TO clearwise_app;");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON consolidation_runs TO clearwise_app;");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
