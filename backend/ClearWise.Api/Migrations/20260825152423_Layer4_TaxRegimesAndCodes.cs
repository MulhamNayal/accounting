using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearWise.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer4_TaxRegimesAndCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tax_code_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "sales_invoice_lines",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "tax_regimes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    input_reclaimable = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_regimes", x => x.id);
                    table.CheckConstraint("ck_tax_regime_dates", "effective_to IS NULL OR effective_to >= effective_from");
                });

            migrationBuilder.CreateTable(
                name: "tax_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_regime_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    output_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_codes", x => x.id);
                    table.CheckConstraint("ck_tax_code_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_tax_code_has_output_account", "rate = 0 OR output_account_id IS NOT NULL");
                    table.CheckConstraint("ck_tax_code_rate", "rate >= 0 AND rate <= 100");
                    table.ForeignKey(
                        name: "fk_tax_codes_accounts_input_account_id",
                        column: x => x.input_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_codes_accounts_output_account_id",
                        column: x => x.output_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_codes_tax_regimes_tax_regime_id",
                        column: x => x.tax_regime_id,
                        principalTable: "tax_regimes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_tax_code_id",
                table: "sales_invoice_lines",
                column: "tax_code_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_line_tax_rate_matches_code",
                table: "sales_invoice_lines",
                sql: "(tax_code_id IS NULL AND tax_rate = 0) OR tax_code_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_codes_input_account_id",
                table: "tax_codes",
                column: "input_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_codes_output_account_id",
                table: "tax_codes",
                column: "output_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_codes_tax_regime_id_code",
                table: "tax_codes",
                columns: new[] { "tax_regime_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_regimes_tenant_id_code",
                table: "tax_regimes",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_tax_code_id",
                table: "sales_invoice_lines",
                column: "tax_code_id",
                principalTable: "tax_codes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropTable(
                name: "tax_codes");

            migrationBuilder.DropTable(
                name: "tax_regimes");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoice_lines_tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_line_tax_rate_matches_code",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "sales_invoice_lines");
        }
    }
}
