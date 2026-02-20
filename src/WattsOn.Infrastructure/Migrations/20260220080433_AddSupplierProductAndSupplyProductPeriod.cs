using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierProductAndSupplyProductPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_margins_supplier_identities_supplier_identity_id",
                table: "supplier_margins");

            migrationBuilder.RenameColumn(
                name: "supplier_identity_id",
                table: "supplier_margins",
                newName: "supplier_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_supplier_margins_supplier_identity_id_timestamp",
                table: "supplier_margins",
                newName: "IX_supplier_margins_supplier_product_id_timestamp");

            migrationBuilder.CreateTable(
                name: "supplier_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_products_supplier_identities_supplier_identity_id",
                        column: x => x.supplier_identity_id,
                        principalTable: "supplier_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_product_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_product_periods", x => x.id);
                    table.ForeignKey(
                        name: "FK_supply_product_periods_supplier_products_supplier_product_id",
                        column: x => x.supplier_product_id,
                        principalTable: "supplier_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supply_product_periods_supplies_supply_id",
                        column: x => x.supply_id,
                        principalTable: "supplies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_products_supplier_identity_id_name",
                table: "supplier_products",
                columns: new[] { "supplier_identity_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supply_product_periods_supplier_product_id",
                table: "supply_product_periods",
                column: "supplier_product_id");

            migrationBuilder.CreateIndex(
                name: "IX_supply_product_periods_supply_id_supplier_product_id",
                table: "supply_product_periods",
                columns: new[] { "supply_id", "supplier_product_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_margins_supplier_products_supplier_product_id",
                table: "supplier_margins",
                column: "supplier_product_id",
                principalTable: "supplier_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_margins_supplier_products_supplier_product_id",
                table: "supplier_margins");

            migrationBuilder.DropTable(
                name: "supply_product_periods");

            migrationBuilder.DropTable(
                name: "supplier_products");

            migrationBuilder.RenameColumn(
                name: "supplier_product_id",
                table: "supplier_margins",
                newName: "supplier_identity_id");

            migrationBuilder.RenameIndex(
                name: "IX_supplier_margins_supplier_product_id_timestamp",
                table: "supplier_margins",
                newName: "IX_supplier_margins_supplier_identity_id_timestamp");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_margins_supplier_identities_supplier_identity_id",
                table: "supplier_margins",
                column: "supplier_identity_id",
                principalTable: "supplier_identities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
