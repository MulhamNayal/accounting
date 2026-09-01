using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Marks the entry that closes a fiscal year, so the profit and loss account can leave it
    /// out. The closing entry is dated inside the year it closes and debits every income
    /// account, so a statement filtered on dates alone would report a closed year as nothing.
    /// </summary>
    /// <remarks>
    /// Nullable, so every existing row correctly gets NULL — there is no defaultValue here to
    /// get wrong. Like <c>reverses_entry_id</c> the link points backwards, from the entry to
    /// the year, so nothing on the year has to be updated for it to exist.
    /// </remarks>
    public partial class Layer9_PeriodClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "closes_fiscal_year_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_closes_fiscal_year_id",
                table: "journal_entries",
                column: "closes_fiscal_year_id",
                filter: "closes_fiscal_year_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_fiscal_years_closes_fiscal_year_id",
                table: "journal_entries",
                column: "closes_fiscal_year_id",
                principalTable: "fiscal_years",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_fiscal_years_closes_fiscal_year_id",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_closes_fiscal_year_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "closes_fiscal_year_id",
                table: "journal_entries");
        }
    }
}
