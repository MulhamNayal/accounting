using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearWise.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer3_ReceiptsAndAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF generated defaultValue: "" here, which would have been wrong. The enum is
            // stored as text, so AccountSystemRole.None is the string "None" - an empty
            // string maps back to no enum member at all and would fail on reading any
            // account that existed before this migration. Corrected by hand.
            migrationBuilder.AddColumn<string>(
                name: "system_role",
                table: "accounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "customer_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_customer_receipts", x => x.id);
                    table.CheckConstraint("ck_receipt_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_receipt_posted_is_complete", "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_customer_receipts_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_receipts_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_receipts_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_receipts_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_allocations", x => x.id);
                    table.CheckConstraint("ck_allocation_amount_nonzero", "amount <> 0");
                    table.CheckConstraint("ck_allocation_sign_matches_kind", "(reverses_allocation_id IS NULL AND amount > 0) OR (reverses_allocation_id IS NOT NULL AND amount < 0)");
                    table.ForeignKey(
                        name: "fk_allocations_allocations_reverses_allocation_id",
                        column: x => x.reverses_allocation_id,
                        principalTable: "allocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocations_customer_receipts_customer_receipt_id",
                        column: x => x.customer_receipt_id,
                        principalTable: "customer_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocations_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocations_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocations_customer_receipt_id",
                table: "allocations",
                column: "customer_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocations_journal_entry_id",
                table: "allocations",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocations_reverses_allocation_id",
                table: "allocations",
                column: "reverses_allocation_id",
                unique: true,
                filter: "reverses_allocation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_allocations_sales_invoice_id",
                table: "allocations",
                column: "sales_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_receipts_bank_account_id",
                table: "customer_receipts",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_receipts_customer_id",
                table: "customer_receipts",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_receipts_journal_entry_id",
                table: "customer_receipts",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_receipts_legal_entity_id_doc_no",
                table: "customer_receipts",
                columns: new[] { "legal_entity_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocations");

            migrationBuilder.DropTable(
                name: "customer_receipts");

            migrationBuilder.DropColumn(
                name: "system_role",
                table: "accounts");
        }
    }
}
