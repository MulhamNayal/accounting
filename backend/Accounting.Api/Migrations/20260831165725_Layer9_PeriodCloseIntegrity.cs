using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Moves the period state machine out of the application and into PostgreSQL:
    /// <list type="number">
    ///   <item>no state change without an event recording it</item>
    ///   <item>hard closed is terminal, for a period and for a year</item>
    ///   <item>a period carrying postings cannot have its dates or ownership moved</item>
    ///   <item>periods and years cannot be deleted</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The design spec defended hard closed being terminal as "not a permission check that
    /// could be granted, an absent code path". An absent code path is application discipline,
    /// which is the one thing this project refuses to rely on anywhere else — the ledger's
    /// immutability, the balance rule and tenant isolation are all enforced below the
    /// application precisely so that a bug, or a support engineer in a hurry, cannot get
    /// round them. The period trail is the answer to the weakness measured in the incumbent
    /// system, so it belongs at the same layer as everything else that answers for itself.
    /// </remarks>
    public partial class Layer9_PeriodCloseIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -----------------------------------------------------------------------
            // 1. Nothing is deleted
            //
            // Without this a hard closed period could be deleted and re-inserted as open,
            // which would make terminality decorative. Neither table is ever deleted from
            // by the application: a period is structure, created once with its year.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("REVOKE DELETE ON periods FROM accounting_app;");
            migrationBuilder.Sql("REVOKE DELETE ON fiscal_years FROM accounting_app;");

            // -----------------------------------------------------------------------
            // 2. Hard closed is terminal, and a period carrying postings is structural
            //
            // Moving a period's dates after entries have been posted into it would change
            // which period an already reported figure belongs to, and would leave those
            // entries outside the period they name - quietly undoing the invariant that
            // journal_entries_period_open checks at insert.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_assert_period_state_transition()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'HardClosed' AND NEW.state <> OLD.state THEN
                        RAISE EXCEPTION
                            'Period % is hard closed. There is no transition out of it: the year is filed.',
                            OLD.id;
                    END IF;

                    IF (NEW.start_date         <> OLD.start_date
                        OR NEW.end_date        <> OLD.end_date
                        OR NEW.fiscal_year_id  <> OLD.fiscal_year_id
                        OR NEW.legal_entity_id <> OLD.legal_entity_id
                        OR NEW.tenant_id       <> OLD.tenant_id)
                       AND EXISTS (SELECT 1 FROM journal_entries WHERE period_id = OLD.id)
                    THEN
                        RAISE EXCEPTION
                            'Period % has postings, so its dates and ownership are fixed. Moving them would change which period an already reported figure belongs to.',
                            OLD.id;
                    END IF;

                    RETURN NEW;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER periods_state_transition
                    BEFORE UPDATE ON periods
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_assert_period_state_transition();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_assert_fiscal_year_state_transition()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state = 'HardClosed' AND NEW.state <> OLD.state THEN
                        RAISE EXCEPTION
                            'Fiscal year % is hard closed and filed. There is no transition out of it.',
                            OLD.code;
                    END IF;

                    RETURN NEW;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER fiscal_years_state_transition
                    BEFORE UPDATE ON fiscal_years
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_assert_fiscal_year_state_transition();
                """);

            // -----------------------------------------------------------------------
            // 3. No state change without an event recording it
            //
            // period_events has been append-only since Layer 0, but nothing ever required
            // a row to be written - so the trail recorded only what a cooperating
            // application chose to record, which is the exact weakness this project exists
            // to close.
            //
            // Deferred to commit, like the balance check, so the update and its event can
            // be written in either order within the transaction.
            //
            // The test is that a matching event is at least as recent as every other event
            // for the period, rather than simply that one exists somewhere. Mere existence
            // would let a close be replayed: close, reopen, then close again writing
            // nothing, with the first close's event still standing as evidence for the
            // third transition. "At least as recent" rather than "the most recent" because
            // two transitions can land on the same microsecond, and a tie should not fail a
            // legitimate write.
            // -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounting_assert_period_transition_recorded()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    recorded    boolean;
                    latest_from text;
                    latest_to   text;
                BEGIN
                    IF OLD.state = NEW.state THEN
                        RETURN NULL;
                    END IF;

                    SELECT EXISTS (
                        SELECT 1
                          FROM period_events e
                         WHERE e.period_id  = NEW.id
                           AND e.from_state = OLD.state
                           AND e.to_state   = NEW.state
                           AND e.at_utc >= (
                                 SELECT MAX(at_utc) FROM period_events
                                  WHERE period_id = NEW.id)
                    ) INTO recorded;

                    IF recorded THEN
                        RETURN NULL;
                    END IF;

                    SELECT from_state, to_state
                      INTO latest_from, latest_to
                      FROM period_events
                     WHERE period_id = NEW.id
                     ORDER BY at_utc DESC
                     LIMIT 1;

                    IF latest_from IS NULL THEN
                        RAISE EXCEPTION
                            'Period % moved from % to % with nothing recorded in period_events. Who closed a period, and why, is not optional.',
                            NEW.id, OLD.state, NEW.state;
                    END IF;

                    RAISE EXCEPTION
                        'Period % moved from % to %, but the most recent recorded event describes % to %. The transition and its record have to be one act.',
                        NEW.id, OLD.state, NEW.state, latest_from, latest_to;
                END $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER periods_transition_recorded
                    AFTER UPDATE ON periods
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW
                    EXECUTE FUNCTION accounting_assert_period_transition_recorded();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS periods_transition_recorded ON periods;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS fiscal_years_state_transition ON fiscal_years;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS periods_state_transition ON periods;");

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_assert_period_transition_recorded();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_assert_fiscal_year_state_transition();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS accounting_assert_period_state_transition();");

            migrationBuilder.Sql("GRANT DELETE ON fiscal_years TO accounting_app;");
            migrationBuilder.Sql("GRANT DELETE ON periods TO accounting_app;");
        }
    }
}
