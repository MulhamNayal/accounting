using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer8_CreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    supplier_credit_note_no = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_credit_notes", x => x.id);
                    table.CheckConstraint("ck_purchase_credit_note_posted_is_complete", "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_purchase_credit_notes_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_credit_notes_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_credit_notes_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_credit_notes_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_credit_notes", x => x.id);
                    table.CheckConstraint("ck_sales_credit_note_posted_is_complete", "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_sales_credit_notes_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_credit_notes_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_credit_notes_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_credit_notes_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_credit_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    charge_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    tax_reclaimable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_credit_note_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_credit_line_price", "unit_price > 0");
                    table.CheckConstraint("ck_purchase_credit_line_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchase_credit_note_lines_accounts_charge_account_id",
                        column: x => x.charge_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_credit_note_lines_purchase_credit_notes_purchase_c",
                        column: x => x.purchase_credit_note_id,
                        principalTable: "purchase_credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_credit_note_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_credit_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_credit_note_lines", x => x.id);
                    table.CheckConstraint("ck_sales_credit_line_price", "unit_price > 0");
                    table.CheckConstraint("ck_sales_credit_line_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sales_credit_note_lines_accounts_revenue_account_id",
                        column: x => x.revenue_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_credit_note_lines_sales_credit_notes_sales_credit_not",
                        column: x => x.sales_credit_note_id,
                        principalTable: "sales_credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sales_credit_note_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_note_lines_charge_account_id",
                table: "purchase_credit_note_lines",
                column: "charge_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_note_lines_purchase_credit_note_id_line_no",
                table: "purchase_credit_note_lines",
                columns: new[] { "purchase_credit_note_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_note_lines_tax_code_id",
                table: "purchase_credit_note_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_notes_journal_entry_id",
                table: "purchase_credit_notes",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_notes_legal_entity_id_doc_no",
                table: "purchase_credit_notes",
                columns: new[] { "legal_entity_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_notes_purchase_invoice_id",
                table: "purchase_credit_notes",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_credit_notes_supplier_id",
                table: "purchase_credit_notes",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_note_lines_revenue_account_id",
                table: "sales_credit_note_lines",
                column: "revenue_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_note_lines_sales_credit_note_id_line_no",
                table: "sales_credit_note_lines",
                columns: new[] { "sales_credit_note_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_note_lines_tax_code_id",
                table: "sales_credit_note_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_notes_customer_id",
                table: "sales_credit_notes",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_notes_journal_entry_id",
                table: "sales_credit_notes",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_notes_legal_entity_id_doc_no",
                table: "sales_credit_notes",
                columns: new[] { "legal_entity_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_credit_notes_sales_invoice_id",
                table: "sales_credit_notes",
                column: "sales_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_credit_note_lines");

            migrationBuilder.DropTable(
                name: "sales_credit_note_lines");

            migrationBuilder.DropTable(
                name: "purchase_credit_notes");

            migrationBuilder.DropTable(
                name: "sales_credit_notes");
        }
    }
}
