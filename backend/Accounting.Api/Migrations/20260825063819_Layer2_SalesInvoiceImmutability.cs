using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for the sales tables, and the one-way door: a draft is ordinary
    /// mutable data, a posted invoice is not.
    /// </summary>
    public partial class Layer2_SalesInvoiceImmutability : Migration
    {
        private static readonly string[] Tables =
            ["customers", "sales_invoices", "sales_invoice_lines"];

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

            // Unlike the ledger, these tables keep UPDATE and DELETE: a draft invoice is
            // meant to be edited and discarded. What must not happen is a *posted* invoice
            // changing, and that is a state condition rather than a privilege, so it needs
            // a trigger rather than a REVOKE.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_sales_invoice_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'Posted' THEN
                        RAISE EXCEPTION
                            'Invoice % is posted and cannot be changed or removed. Issue a credit note instead.',
                            COALESCE(OLD.doc_no, OLD.id::text);
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            // The Draft -> Posted transition is itself an UPDATE, and it passes because at
            // that moment OLD.state is still 'Draft'. Every later attempt fails.
            migrationBuilder.Sql("""
                CREATE TRIGGER sales_invoices_frozen_once_posted
                    BEFORE UPDATE OR DELETE ON sales_invoices
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_sales_invoice_frozen_once_posted();
                """);

            // Lines are guarded through their parent: freezing the header while leaving the
            // lines editable would let the invoice total drift away from the entry it posted.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_invoice_line_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE parent_state text;
                BEGIN
                    SELECT state INTO parent_state
                      FROM sales_invoices
                     WHERE id = COALESCE(NEW.sales_invoice_id, OLD.sales_invoice_id);

                    IF parent_state = 'Posted' THEN
                        RAISE EXCEPTION
                            'The invoice is posted; its lines cannot be added to, changed or removed.';
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER sales_invoice_lines_frozen_once_posted
                    BEFORE INSERT OR UPDATE OR DELETE ON sales_invoice_lines
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_invoice_line_frozen_once_posted();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS sales_invoice_lines_frozen_once_posted ON sales_invoice_lines;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS sales_invoices_frozen_once_posted ON sales_invoices;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS accounting_invoice_line_frozen_once_posted();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS accounting_sales_invoice_frozen_once_posted();");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
