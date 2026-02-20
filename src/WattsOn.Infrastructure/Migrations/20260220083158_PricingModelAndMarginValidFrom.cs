using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PricingModelAndMarginValidFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "timestamp",
                table: "supplier_margins",
                newName: "valid_from");

            migrationBuilder.RenameIndex(
                name: "IX_supplier_margins_supplier_product_id_timestamp",
                table: "supplier_margins",
                newName: "IX_supplier_margins_supplier_product_id_valid_from");

            migrationBuilder.AddColumn<string>(
                name: "pricing_model",
                table: "supplier_products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SpotAddon");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pricing_model",
                table: "supplier_products");

            migrationBuilder.RenameColumn(
                name: "valid_from",
                table: "supplier_margins",
                newName: "timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_supplier_margins_supplier_product_id_valid_from",
                table: "supplier_margins",
                newName: "IX_supplier_margins_supplier_product_id_timestamp");
        }
    }
}
