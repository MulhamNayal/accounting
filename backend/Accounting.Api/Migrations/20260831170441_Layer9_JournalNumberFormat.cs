using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Puts the year into the journal entry number format, on series that were seeded without
    /// it.
    /// </summary>
    /// <remarks>
    /// The journal series resets yearly but its format carried no year, so the counter
    /// restarted each January onto numbers that already existed and the unique index on
    /// (legal_entity_id, entry_no) refused them — the first posting of a new financial year
    /// would fail. Latent until now because nothing had crossed a year boundary; period close
    /// is what makes doing so routine. Every other seeded series already carries the year.
    /// <para>
    /// No issued number changes. Entry numbers are stored on the entries themselves, so this
    /// affects only what is allocated next and restates nothing.
    /// </para>
    /// </remarks>
    public partial class Layer9_JournalNumberFormat : Migration
    {
        private const string WithoutYear = "JV-{0:D5}";
        private const string WithYear = "JV-{1:yyyy}-{0:D5}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Rewrite(from: WithoutYear, to: WithYear));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Rewrite(from: WithYear, to: WithoutYear));

        /// <summary>
        /// Rewrites the format one tenant at a time.
        /// </summary>
        /// <remarks>
        /// number_series is FORCE ROW LEVEL SECURITY, so the owner role running this migration
        /// is subject to the tenant policy like anyone else — a plain UPDATE would match no
        /// rows at all, silently, because app.current_tenant is unset during a migration. So
        /// the loop sets it per tenant, using the mechanism as designed rather than turning it
        /// off. Reading the tenant list works because <c>tenants</c> is deliberately ENABLE
        /// without FORCE, precisely so the owner can do cross-tenant work.
        /// </remarks>
        private static string Rewrite(string from, string to) => $"""
            DO $do$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT id FROM tenants LOOP
                    PERFORM set_config('app.current_tenant', t.id::text, true);

                    UPDATE number_series
                       SET format = '{to}'
                     WHERE document_type = 'JournalEntry'
                       AND format = '{from}';
                END LOOP;
            END $do$;
            """;
    }
}
