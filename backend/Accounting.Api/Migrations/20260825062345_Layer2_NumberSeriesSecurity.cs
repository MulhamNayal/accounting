using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for the numbering tables, and removal of the interim sequence the
    /// number series replaces.
    /// </summary>
    public partial class Layer2_NumberSeriesSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "number_series", "number_counters" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {table} "
                    + "USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) "
                    + "WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);");
            }

            // number_counters is the one table the application legitimately updates in
            // place: incrementing a counter is not an accounting fact, it is bookkeeping
            // about bookkeeping. UPDATE stays granted here, unlike on the ledger.
            //
            // DELETE does not. Removing a counter would silently restart a series and
            // reissue numbers already on issued documents.
            migrationBuilder.Sql("REVOKE DELETE ON number_counters FROM clearwise_app;");

            // number_series keeps ordinary DML for now. Restricting it to an administrative
            // role is the right end state, but there is no such role or path yet, and
            // revoking it here would only break the code that legitimately creates series.
            // A lock with no key is worse than no lock.

            // Superseded by number_series. Dropped rather than left behind, so there is no
            // second source of document numbers for someone to reach for later.
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS journal_entry_no_seq;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS journal_entry_no_seq START WITH 1 INCREMENT BY 1;");
            migrationBuilder.Sql("GRANT USAGE, SELECT ON SEQUENCE journal_entry_no_seq TO clearwise_app;");

            migrationBuilder.Sql("GRANT DELETE ON number_counters TO clearwise_app;");

            foreach (var table in new[] { "number_series", "number_counters" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
