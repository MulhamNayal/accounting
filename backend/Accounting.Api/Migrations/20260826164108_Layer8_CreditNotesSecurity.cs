using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for credit notes, and the one-way door on a posted one.
    /// </summary>
    /// <remarks>
    /// A credit note is the mechanism for undoing an invoice, which makes it the document most
    /// worth freezing. If a posted credit could be edited, the invoice would be effectively
    /// mutable again through the back door, and the guarantee that a posted figure cannot
    /// change quietly would be worth nothing.
    /// </remarks>
    public partial class Layer8_CreditNotesSecurity : Migration
    {
        private static readonly string[] Tables =
        [
            "sales_credit_notes",
            "sales_credit_note_lines",
            "purchase_credit_notes",
            "purchase_credit_note_lines",
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

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_credit_note_frozen_once_posted()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'Posted' THEN
                        RAISE EXCEPTION
                            'Credit note % is posted and cannot be changed or removed. Raise another document instead.',
                            COALESCE(OLD.doc_no, OLD.id::text);
                    END IF;
                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            // The Draft -> Posted transition passes, because OLD.state is still 'Draft' at
            // that moment. Every later attempt fails.
            foreach (var table in new[] { "sales_credit_notes", "purchase_credit_notes" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER {table}_frozen_once_posted
                        BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW
                        EXECUTE FUNCTION accounting_credit_note_frozen_once_posted();
                    """);
            }

            // The lines are frozen with the header. Leaving them editable would mean a credit
            // note that looks untouched while the amounts underneath it changed, which is worse
            // than no protection because it is invisible.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_sales_credit_line_frozen()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE note_state text;
                BEGIN
                    SELECT state INTO note_state FROM sales_credit_notes
                     WHERE id = COALESCE(NEW.sales_credit_note_id, OLD.sales_credit_note_id);

                    IF note_state = 'Posted' THEN
                        RAISE EXCEPTION
                            'That credit note is posted; its lines cannot be changed or removed.';
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER sales_credit_note_lines_frozen
                    BEFORE INSERT OR UPDATE OR DELETE ON sales_credit_note_lines
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_sales_credit_line_frozen();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_purchase_credit_line_frozen()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE note_state text;
                BEGIN
                    SELECT state INTO note_state FROM purchase_credit_notes
                     WHERE id = COALESCE(NEW.purchase_credit_note_id, OLD.purchase_credit_note_id);

                    IF note_state = 'Posted' THEN
                        RAISE EXCEPTION
                            'That credit note is posted; its lines cannot be changed or removed.';
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER purchase_credit_note_lines_frozen
                    BEFORE INSERT OR UPDATE OR DELETE ON purchase_credit_note_lines
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_purchase_credit_line_frozen();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS purchase_credit_note_lines_frozen ON purchase_credit_note_lines;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_purchase_credit_line_frozen();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS sales_credit_note_lines_frozen ON sales_credit_note_lines;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_sales_credit_line_frozen();");

            foreach (var table in new[] { "sales_credit_notes", "purchase_credit_notes" })
            {
                migrationBuilder.Sql(
                    $"DROP TRIGGER IF EXISTS {table}_frozen_once_posted ON {table};");
            }

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_credit_note_frozen_once_posted();");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
