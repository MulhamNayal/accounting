using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Tenant isolation for the tax tables, and protection for codes already used.
    /// </summary>
    public partial class Layer4_TaxSecurity : Migration
    {
        private static readonly string[] Tables = ["tax_regimes", "tax_codes"];

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

            // A code's rate must never change once anything has been posted under it.
            // Postings store the code, not the rate, so editing a rate would silently
            // restate the tax on every historical document that used it — including returns
            // already filed. Retiring a code and adding a replacement is the only correct
            // way to change a rate.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_tax_code_rate_is_final()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE used boolean;
                BEGIN
                    IF NEW.rate = OLD.rate THEN
                        RETURN NEW;
                    END IF;

                    SELECT EXISTS (SELECT 1 FROM postings WHERE tax_code_id = OLD.id)
                      INTO used;

                    IF used THEN
                        RAISE EXCEPTION
                            'Tax code % has been posted under and its rate cannot change. Retire it with an effective_to date and add a replacement.',
                            OLD.code;
                    END IF;

                    RETURN NEW;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER tax_codes_rate_is_final
                    BEFORE UPDATE ON tax_codes
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_tax_code_rate_is_final();
                """);

            // Deleting a used code would orphan the tax_code_id on historical postings, so
            // a return could no longer be reconstructed from the ledger.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_tax_code_not_deletable_if_used()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF EXISTS (SELECT 1 FROM postings WHERE tax_code_id = OLD.id) THEN
                        RAISE EXCEPTION
                            'Tax code % has been posted under and cannot be deleted. Set effective_to instead.',
                            OLD.code;
                    END IF;
                    RETURN OLD;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER tax_codes_not_deletable_if_used
                    BEFORE DELETE ON tax_codes
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_tax_code_not_deletable_if_used();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tax_codes_not_deletable_if_used ON tax_codes;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tax_codes_rate_is_final ON tax_codes;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS accounting_tax_code_not_deletable_if_used();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS accounting_tax_code_rate_is_final();");

            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
