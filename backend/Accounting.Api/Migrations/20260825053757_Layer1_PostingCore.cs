using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer1_PostingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supersedes_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.CheckConstraint("ck_journal_entry_no_self_reference", "(reverses_entry_id IS NULL OR reverses_entry_id <> id) AND (supersedes_entry_id IS NULL OR supersedes_entry_id <> id)");
                    table.CheckConstraint("ck_journal_entry_reversal_has_reason", "reverses_entry_id IS NULL OR reason_code IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_journal_entries_journal_entries_reverses_entry_id",
                        column: x => x.reverses_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_journal_entries_supersedes_entry_id",
                        column: x => x.supersedes_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_users_posted_by_user_id",
                        column: x => x.posted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    intercompany_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postings", x => x.id);
                    table.CheckConstraint("ck_posting_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_posting_functional_amount_positive", "functional_amount > 0");
                    table.CheckConstraint("ck_posting_fx_rate_positive", "fx_rate > 0");
                    table.ForeignKey(
                        name: "fk_postings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_postings_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_postings_legal_entities_intercompany_entity_id",
                        column: x => x.intercompany_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_legal_entity_id_entry_date",
                table: "journal_entries",
                columns: new[] { "legal_entity_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_legal_entity_id_entry_no",
                table: "journal_entries",
                columns: new[] { "legal_entity_id", "entry_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_period_id",
                table: "journal_entries",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_posted_by_user_id",
                table: "journal_entries",
                column: "posted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_reverses_entry_id",
                table: "journal_entries",
                column: "reverses_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_supersedes_entry_id",
                table: "journal_entries",
                column: "supersedes_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_postings_account_id",
                table: "postings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_postings_intercompany_entity_id",
                table: "postings",
                column: "intercompany_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_postings_journal_entry_id_line_no",
                table: "postings",
                columns: new[] { "journal_entry_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_postings_legal_entity_id_account_id",
                table: "postings",
                columns: new[] { "legal_entity_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_postings_legal_entity_id_customer_id",
                table: "postings",
                columns: new[] { "legal_entity_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_postings_legal_entity_id_supplier_id",
                table: "postings",
                columns: new[] { "legal_entity_id", "supplier_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "postings");

            migrationBuilder.DropTable(
                name: "journal_entries");
        }
    }
}
