using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <inheritdoc />
    public partial class Layer2_NumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    format = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reset_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_gapless = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_series", x => x.id);
                    table.ForeignKey(
                        name: "fk_number_series_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "number_counters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    next_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_counters", x => x.id);
                    table.CheckConstraint("ck_number_counter_positive", "next_number > 0");
                    table.ForeignKey(
                        name: "fk_number_counters_number_series_number_series_id",
                        column: x => x.number_series_id,
                        principalTable: "number_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_number_counters_number_series_id_period_key",
                table: "number_counters",
                columns: new[] { "number_series_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_number_series_legal_entity_id_code",
                table: "number_series",
                columns: new[] { "legal_entity_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_number_series_legal_entity_id_document_type_is_active",
                table: "number_series",
                columns: new[] { "legal_entity_id", "document_type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_counters");

            migrationBuilder.DropTable(
                name: "number_series");
        }
    }
}
