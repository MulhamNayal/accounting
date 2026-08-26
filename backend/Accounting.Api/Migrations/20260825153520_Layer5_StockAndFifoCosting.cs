using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer5_StockAndFifoCosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    base_uom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inventory_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_of_sales_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_accounts_cost_of_sales_account_id",
                        column: x => x.cost_of_sales_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_accounts_inventory_account_id",
                        column: x => x.inventory_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_moves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    moved_on = table.Column<DateOnly>(type: "date", nullable: false),
                    source_document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_moves", x => x.id);
                    table.CheckConstraint("ck_stock_move_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_moves_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_moves_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_layers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_move_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    adjusts_layer_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_layers", x => x.id);
                    table.CheckConstraint("ck_cost_layer_cost", "unit_cost > 0");
                    table.CheckConstraint("ck_cost_layer_quantity_matches_kind", "(adjusts_layer_id IS NULL AND quantity_received > 0) OR (adjusts_layer_id IS NOT NULL AND quantity_received = 0)");
                    table.ForeignKey(
                        name: "fk_cost_layers_cost_layers_adjusts_layer_id",
                        column: x => x.adjusts_layer_id,
                        principalTable: "cost_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_layers_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_layers_stock_moves_source_move_id",
                        column: x => x.source_move_id,
                        principalTable: "stock_moves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_consumptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    out_move_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_consumptions", x => x.id);
                    table.CheckConstraint("ck_cost_consumption_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_cost_consumptions_cost_layers_cost_layer_id",
                        column: x => x.cost_layer_id,
                        principalTable: "cost_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_consumptions_stock_moves_out_move_id",
                        column: x => x.out_move_id,
                        principalTable: "stock_moves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cost_consumptions_cost_layer_id",
                table: "cost_consumptions",
                column: "cost_layer_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_consumptions_out_move_id",
                table: "cost_consumptions",
                column: "out_move_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_layers_adjusts_layer_id",
                table: "cost_layers",
                column: "adjusts_layer_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_layers_item_id_sequence",
                table: "cost_layers",
                columns: new[] { "item_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_layers_legal_entity_id_item_id",
                table: "cost_layers",
                columns: new[] { "legal_entity_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_layers_source_move_id",
                table: "cost_layers",
                column: "source_move_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_cost_of_sales_account_id",
                table: "items",
                column: "cost_of_sales_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_inventory_account_id",
                table: "items",
                column: "inventory_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_id_code",
                table: "items",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_moves_item_id",
                table: "stock_moves",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_moves_journal_entry_id",
                table: "stock_moves",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_moves_legal_entity_id_item_id_moved_on",
                table: "stock_moves",
                columns: new[] { "legal_entity_id", "item_id", "moved_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_consumptions");

            migrationBuilder.DropTable(
                name: "cost_layers");

            migrationBuilder.DropTable(
                name: "stock_moves");

            migrationBuilder.DropTable(
                name: "items");
        }
    }
}
