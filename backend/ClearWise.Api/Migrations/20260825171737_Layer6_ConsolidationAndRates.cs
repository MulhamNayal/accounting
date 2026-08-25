using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearWise.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer6_ConsolidationAndRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consolidation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    presentation_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consolidation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    closing_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    average_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: true),
                    source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rate_distinct_currencies", "from_currency <> to_currency");
                    table.CheckConstraint("ck_exchange_rate_positive", "closing_rate > 0 AND (average_rate IS NULL OR average_rate > 0)");
                });

            migrationBuilder.CreateTable(
                name: "consolidation_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consolidation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    presentation_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_used = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consolidation_postings", x => x.id);
                    table.CheckConstraint("ck_consolidation_line_entity", "legal_entity_id IS NOT NULL OR kind = 'Translation'");
                    table.ForeignKey(
                        name: "fk_consolidation_postings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consolidation_postings_consolidation_runs_consolidation_run",
                        column: x => x.consolidation_run_id,
                        principalTable: "consolidation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_consolidation_postings_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_postings_account_id",
                table: "consolidation_postings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_postings_consolidation_run_id",
                table: "consolidation_postings",
                column: "consolidation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_postings_legal_entity_id",
                table: "consolidation_postings",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_runs_tenant_id_as_of",
                table: "consolidation_runs",
                columns: new[] { "tenant_id", "as_of" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_from_currency_to_currency_rate_date",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "from_currency", "to_currency", "rate_date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_postings_legal_entities_legal_entity_id",
                table: "postings",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_postings_legal_entities_legal_entity_id",
                table: "postings");

            migrationBuilder.DropTable(
                name: "consolidation_postings");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "consolidation_runs");
        }
    }
}
