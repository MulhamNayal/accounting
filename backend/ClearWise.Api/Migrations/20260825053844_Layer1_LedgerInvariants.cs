using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearWise.Api.Migrations
{
    /// <summary>
    /// The guarantees that make ClearWise's ledger trustworthy, all enforced by PostgreSQL
    /// rather than by application discipline:
    /// <list type="number">
    ///   <item>every entry balances, checked at commit</item>
    ///   <item>postings are append-only — UPDATE and DELETE are revoked</item>
    ///   <item>a posting to a control account must carry its dimension</item>
    ///   <item>nothing posts into a closed period</item>
    ///   <item>tenant isolation, as everywhere else</item>
    /// </list>
    /// </summary>
    public partial class Layer1_LedgerInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -----------------------------------------------------------------------
            // 1. Tenant isolation
            // -----------------------------------------------------------------------
            foreach (var table in new[] { "journal_entries", "postings" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {table} "
                    + "USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) "
                    + "WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);");
            }

            // -----------------------------------------------------------------------
            // 2. Immutability
            //
            // Layer 0 set default privileges granting the application role full DML on
            // new tables, so this revocation is doing real work. After it the application
            // can append to the ledger and read it, and has no means to alter or remove a
            // posted row - not by bug, not by malice, not by a support engineer in a hurry.
            //
            // This is the specific weakness measured in the incumbent system, where the
            // audit trail existed only because the application chose to write it.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON journal_entries FROM clearwise_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON postings FROM clearwise_app;");

            // -----------------------------------------------------------------------
            // 3. Every entry balances
            //
            // A CONSTRAINT TRIGGER deferred to commit, so a multi-row insert is legal
            // while it is still in progress and only the finished entry is judged.
            //
            // Balance is asserted in functional currency only. Transaction-currency
            // amounts need not sum to zero across a multi-currency entry - they are
            // different units, and adding them would be meaningless.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_assert_entry_balanced()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    entry      uuid;
                    line_count integer;
                    imbalance  numeric(19,4);
                BEGIN
                    entry := NEW.journal_entry_id;

                    SELECT COUNT(*),
                           COALESCE(SUM(CASE WHEN direction = 'Debit' THEN functional_amount
                                             ELSE -functional_amount END), 0)
                      INTO line_count, imbalance
                      FROM postings
                     WHERE journal_entry_id = entry;

                    IF line_count < 2 THEN
                        RAISE EXCEPTION
                            'Journal entry % has % posting(s); an entry needs at least two.',
                            entry, line_count;
                    END IF;

                    IF imbalance <> 0 THEN
                        RAISE EXCEPTION
                            'Journal entry % does not balance: debits minus credits = %.',
                            entry, imbalance;
                    END IF;

                    RETURN NULL;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER postings_entry_balanced
                    AFTER INSERT ON postings
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW
                    EXECUTE FUNCTION clearwise_assert_entry_balanced();
                """);

            // An entry with no postings at all would never fire the trigger above, so it
            // is caught from the other side.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_assert_entry_has_postings()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE line_count integer;
                BEGIN
                    SELECT COUNT(*) INTO line_count
                      FROM postings WHERE journal_entry_id = NEW.id;

                    IF line_count < 2 THEN
                        RAISE EXCEPTION
                            'Journal entry % was committed with % posting(s); at least two are required.',
                            NEW.id, line_count;
                    END IF;

                    RETURN NULL;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER journal_entries_have_postings
                    AFTER INSERT ON journal_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW
                    EXECUTE FUNCTION clearwise_assert_entry_has_postings();
                """);

            // -----------------------------------------------------------------------
            // 4. Control accounts carry their dimension
            //
            // Without this a receivables posting with no customer is invisible to the
            // derived subledger while still counting toward the control account - which
            // recreates exactly the drift this design exists to prevent.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_assert_posting_valid()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    account_control  text;
                    account_postable boolean;
                    account_code     text;
                BEGIN
                    SELECT control_type, is_postable, code
                      INTO account_control, account_postable, account_code
                      FROM accounts WHERE id = NEW.account_id;

                    IF account_postable IS NULL THEN
                        RAISE EXCEPTION 'Posting references account % which does not exist.',
                            NEW.account_id;
                    END IF;

                    IF NOT account_postable THEN
                        RAISE EXCEPTION
                            'Account % is a heading and cannot be posted to; post to one of its children.',
                            account_code;
                    END IF;

                    IF account_control = 'AccountsReceivable' AND NEW.customer_id IS NULL THEN
                        RAISE EXCEPTION
                            'Account % is a receivables control account; the posting must name a customer.',
                            account_code;
                    ELSIF account_control = 'AccountsPayable' AND NEW.supplier_id IS NULL THEN
                        RAISE EXCEPTION
                            'Account % is a payables control account; the posting must name a supplier.',
                            account_code;
                    ELSIF account_control = 'Stock' AND NEW.item_id IS NULL THEN
                        RAISE EXCEPTION
                            'Account % is a stock control account; the posting must name an item.',
                            account_code;
                    END IF;

                    RETURN NEW;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER postings_valid
                    BEFORE INSERT ON postings
                    FOR EACH ROW
                    EXECUTE FUNCTION clearwise_assert_posting_valid();
                """);

            // -----------------------------------------------------------------------
            // 5. Nothing posts into a closed period
            //
            // Also checks the entry date actually falls inside the period it claims, so a
            // caller cannot sidestep a closed month by pointing an out-of-range date at an
            // open one.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_assert_period_open()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    period_state    text;
                    period_start    date;
                    period_end      date;
                    period_entity   uuid;
                BEGIN
                    SELECT state, start_date, end_date, legal_entity_id
                      INTO period_state, period_start, period_end, period_entity
                      FROM periods WHERE id = NEW.period_id;

                    IF period_state IS NULL THEN
                        RAISE EXCEPTION 'Journal entry references period % which does not exist.',
                            NEW.period_id;
                    END IF;

                    IF period_entity <> NEW.legal_entity_id THEN
                        RAISE EXCEPTION
                            'Period % belongs to a different entity than the entry.', NEW.period_id;
                    END IF;

                    IF period_state <> 'Open' THEN
                        RAISE EXCEPTION
                            'Period % is % and does not accept postings.',
                            NEW.period_id, period_state;
                    END IF;

                    IF NEW.entry_date < period_start OR NEW.entry_date > period_end THEN
                        RAISE EXCEPTION
                            'Entry date % is outside period % (% to %).',
                            NEW.entry_date, NEW.period_id, period_start, period_end;
                    END IF;

                    RETURN NEW;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER journal_entries_period_open
                    BEFORE INSERT ON journal_entries
                    FOR EACH ROW
                    EXECUTE FUNCTION clearwise_assert_period_open();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_entries_period_open ON journal_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_entries_have_postings ON journal_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS postings_valid ON postings;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS postings_entry_balanced ON postings;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_assert_period_open();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_assert_posting_valid();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_assert_entry_has_postings();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_assert_entry_balanced();");

            migrationBuilder.Sql("GRANT UPDATE, DELETE ON postings TO clearwise_app;");
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON journal_entries TO clearwise_app;");

            foreach (var table in new[] { "journal_entries", "postings" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
