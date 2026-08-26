using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for the stock tables, and append-only cost history.
    /// </summary>
    public partial class Layer5_StockSecurity : Migration
    {
        private static readonly string[] Tables =
            ["items", "stock_moves", "cost_layers", "cost_consumptions"];

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

            // Cost history is append-only, for the same reason the ledger is: a movement and
            // its cost posted together, and altering either afterwards would leave stock
            // valued at something the inventory account never recorded.
            //
            // This is also what makes the absence of a quantity_remaining column safe.
            // Remaining is derived from these rows, so if they cannot change, the derivation
            // cannot silently drift.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON stock_moves FROM accounting_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON cost_layers FROM accounting_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON cost_consumptions FROM accounting_app;");

            // A layer must never be consumed beyond what it received. Without this, a
            // concurrent pair of issues could each see the same stock available and both
            // take it, costing goods that were never there.
            //
            // Serialised with an advisory lock rather than SELECT ... FOR UPDATE, because
            // row locking needs UPDATE privilege and the revocation above deliberately
            // removed it. An advisory lock needs no table privilege, achieves the same
            // ordering, and releases at end of transaction on its own.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_cost_layer_not_overconsumed()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    received decimal(19,4);
                    consumed decimal(19,4);
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        ('x' || substr(md5(NEW.cost_layer_id::text), 1, 16))::bit(64)::bigint);

                    SELECT quantity_received INTO received
                      FROM cost_layers WHERE id = NEW.cost_layer_id;

                    SELECT COALESCE(SUM(quantity), 0) INTO consumed
                      FROM cost_consumptions WHERE cost_layer_id = NEW.cost_layer_id;

                    IF consumed > received THEN
                        RAISE EXCEPTION
                            'Cost layer % has % received but % consumed. Stock cannot be issued twice.',
                            NEW.cost_layer_id, received, consumed;
                    END IF;

                    RETURN NULL;
                END $fn$;
                """);

            // Deferred to commit so a multi-layer issue is judged once, complete, rather
            // than part-way through.
            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER cost_consumptions_within_layer
                    AFTER INSERT ON cost_consumptions
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_cost_layer_not_overconsumed();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS cost_consumptions_within_layer ON cost_consumptions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS accounting_cost_layer_not_overconsumed();");

            migrationBuilder.Sql("GRANT UPDATE, DELETE ON cost_consumptions TO accounting_app;");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON cost_layers TO accounting_app;");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON stock_moves TO accounting_app;");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
