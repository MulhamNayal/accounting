using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for receipts and allocations, the one-way door on a posted receipt,
    /// and append-only allocations.
    /// </summary>
    public partial class Layer3_ReceivablesSecurity : Migration
    {
        private static readonly string[] Tables = ["customer_receipts", "allocations"];

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

            // Allocations are append-only. Undoing one inserts a reversing row, because how
            // money was applied is itself a fact: a customer disputing which invoice their
            // payment cleared is a real conversation, and "we changed our minds and kept no
            // record" is not an answer to it.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON allocations FROM clearwise_app;");

            // A draft receipt is ordinary mutable data. A posted one is not â€” same one-way
            // door as a sales invoice, and a state condition rather than a privilege, so it
            // needs a trigger rather than a REVOKE.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_receipt_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'Posted' THEN
                        RAISE EXCEPTION
                            'Receipt % is posted and cannot be changed or removed. Unallocate it and issue a refund instead.',
                            COALESCE(OLD.doc_no, OLD.id::text);
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            // The Draft -> Posted transition itself passes, because at that moment OLD.state
            // is still 'Draft'. Every later attempt fails.
            migrationBuilder.Sql("""
                CREATE TRIGGER customer_receipts_frozen_once_posted
                    BEFORE UPDATE OR DELETE ON customer_receipts
                    FOR EACH ROW
                    EXECUTE FUNCTION clearwise_receipt_frozen_once_posted();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS customer_receipts_frozen_once_posted ON customer_receipts;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_receipt_frozen_once_posted();");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON allocations TO clearwise_app;");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
