using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer7_Payables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    credit_term_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.CheckConstraint("ck_supplier_credit_terms", "credit_term_days >= 0");
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    supplier_invoice_no = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoices", x => x.id);
                    table.CheckConstraint("ck_purchase_invoice_due_after_doc", "due_date >= doc_date");
                    table.CheckConstraint("ck_purchase_invoice_posted_is_complete", "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_purchase_invoices_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_payments", x => x.id);
                    table.CheckConstraint("ck_payment_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payment_posted_is_complete", "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_supplier_payments_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_purchase_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_line_price", "unit_price > 0");
                    table.CheckConstraint("ck_purchase_line_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_accounts_charge_account_id",
                        column: x => x.charge_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    fx_gain_loss_functional = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reverses_allocation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocation_amount_nonzero", "amount <> 0");
                    table.CheckConstraint("ck_payment_allocation_sign_matches_kind", "(reverses_allocation_id IS NULL AND amount > 0) OR (reverses_allocation_id IS NOT NULL AND amount < 0)");
                    table.ForeignKey(
                        name: "fk_payment_allocations_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payment_allocations_reverses_allocation",
                        column: x => x.reverses_allocation_id,
                        principalTable: "payment_allocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_supplier_payments_supplier_payment_id",
                        column: x => x.supplier_payment_id,
                        principalTable: "supplier_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_journal_entry_id",
                table: "payment_allocations",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_purchase_invoice_id",
                table: "payment_allocations",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_reverses_allocation_id",
                table: "payment_allocations",
                column: "reverses_allocation_id",
                unique: true,
                filter: "reverses_allocation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_supplier_payment_id",
                table: "payment_allocations",
                column: "supplier_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_charge_account_id",
                table: "purchase_invoice_lines",
                column: "charge_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_purchase_invoice_id_line_no",
                table: "purchase_invoice_lines",
                columns: new[] { "purchase_invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_tax_code_id",
                table: "purchase_invoice_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_journal_entry_id",
                table: "purchase_invoices",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_legal_entity_id_doc_date",
                table: "purchase_invoices",
                columns: new[] { "legal_entity_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_legal_entity_id_doc_no",
                table: "purchase_invoices",
                columns: new[] { "legal_entity_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_id_supplier_invoice_no",
                table: "purchase_invoices",
                columns: new[] { "supplier_id", "supplier_invoice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_bank_account_id",
                table: "supplier_payments",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_journal_entry_id",
                table: "supplier_payments",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_legal_entity_id_doc_no",
                table: "supplier_payments",
                columns: new[] { "legal_entity_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_supplier_id",
                table: "supplier_payments",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id_code",
                table: "suppliers",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "purchase_invoice_lines");

            migrationBuilder.DropTable(
                name: "supplier_payments");

            migrationBuilder.DropTable(
                name: "purchase_invoices");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
