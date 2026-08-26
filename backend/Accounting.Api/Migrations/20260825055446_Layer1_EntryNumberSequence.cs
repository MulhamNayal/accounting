using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// An interim source of journal entry numbers.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately gappy and tenant-agnostic.</b> A PostgreSQL sequence does not roll
    /// back with its transaction, so a failed post burns a number. That is acceptable for
    /// manual journals, which no tax authority examines for density.
    /// <para>
    /// Layer 2 replaces this with proper number series: per entity, per document type,
    /// format templates, yearly resets, and genuinely gap-free allocation for the documents
    /// that legally require it. This exists only so entries can be posted before that
    /// arrives, and must not be used for sales invoices or credit notes.
    /// </para>
    /// </remarks>
    public partial class Layer1_EntryNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE journal_entry_no_seq START WITH 1 INCREMENT BY 1;");
            migrationBuilder.Sql("GRANT USAGE, SELECT ON SEQUENCE journal_entry_no_seq TO clearwise_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS journal_entry_no_seq;");
        }
    }
}
