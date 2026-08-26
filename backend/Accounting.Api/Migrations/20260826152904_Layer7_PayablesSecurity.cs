using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for the payables tables, the one-way door on a posted bill and a
    /// posted payment, and append-only payment allocations.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape as Layers 2 and 3 gave the sales side. Payables is where
    /// money leaves the business, so it is the half of the ledger most worth being able to
    /// prove untampered — a guarantee covering sales but not purchases would protect the
    /// wrong direction.
    /// </remarks>
    public partial class Layer7_PayablesSecurity : Migration
    {
        private static readonly string[] Tables =
        [
            "suppliers",
            "purchase_invoices",
            "purchase_invoice_lines",
            "supplier_payments",
            "payment_allocations",
        ];

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

            // Append-only, matching allocations on the receivables side. Which bill a payment
            // settled is a decision somebody made, and a supplier querying it deserves a
            // record rather than an amended one.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON payment_allocations FROM accounting_app;");

            // A draft is ordinary mutable data; a posted document is not. This is a condition
            // on state rather than a privilege, so it needs a trigger rather than a REVOKE.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_purchase_invoice_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'Posted' THEN
                        RAISE EXCEPTION
                            'Purchase invoice % is posted and cannot be changed or removed. Record a supplier credit note instead.',
                            COALESCE(OLD.doc_no, OLD.supplier_invoice_no);
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            // The Draft -> Posted transition passes, because at that moment OLD.state is still
            // 'Draft'. Every later attempt fails.
            migrationBuilder.Sql("""
                CREATE TRIGGER purchase_invoices_frozen_once_posted
                    BEFORE UPDATE OR DELETE ON purchase_invoices
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_purchase_invoice_frozen_once_posted();
                """);

            // A posted bill's lines are frozen too. Without this the header would be immutable
            // while its amounts could still be edited underneath it, which is worse than no
            // protection at all -- the document would look untouched.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_purchase_line_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    invoice_state text;
                BEGIN
                    SELECT state INTO invoice_state
                      FROM purchase_invoices
                     WHERE id = COALESCE(NEW.purchase_invoice_id, OLD.purchase_invoice_id);

                    IF invoice_state = 'Posted' THEN
                        RAISE EXCEPTION
                            'That purchase invoice is posted; its lines cannot be changed or removed.';
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER purchase_invoice_lines_frozen_once_posted
                    BEFORE INSERT OR UPDATE OR DELETE ON purchase_invoice_lines
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_purchase_line_frozen_once_posted();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_payment_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'Posted' THEN
                        RAISE EXCEPTION
                            'Payment % is posted and cannot be changed or removed. Unallocate it and record a refund instead.',
                            COALESCE(OLD.doc_no, OLD.id::text);
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER supplier_payments_frozen_once_posted
                    BEFORE UPDATE OR DELETE ON supplier_payments
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_payment_frozen_once_posted();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS supplier_payments_frozen_once_posted ON supplier_payments;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_payment_frozen_once_posted();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS purchase_invoice_lines_frozen_once_posted ON purchase_invoice_lines;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_purchase_line_frozen_once_posted();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS purchase_invoices_frozen_once_posted ON purchase_invoices;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_purchase_invoice_frozen_once_posted();");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON payment_allocations TO accounting_app;");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
