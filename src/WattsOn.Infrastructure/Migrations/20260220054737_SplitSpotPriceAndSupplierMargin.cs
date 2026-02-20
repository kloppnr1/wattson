using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitSpotPriceAndSupplierMargin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "pris_id",
                table: "settlement_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "settlement_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "prices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "spot_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_area = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_dkk_per_kwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spot_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_margins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_dkk_per_kwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_margins", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_margins_supplier_identities_supplier_identity_id",
                        column: x => x.supplier_identity_id,
                        principalTable: "supplier_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_spot_prices_price_area_timestamp",
                table: "spot_prices",
                columns: new[] { "price_area", "timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_margins_supplier_identity_id_timestamp",
                table: "supplier_margins",
                columns: new[] { "supplier_identity_id", "timestamp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spot_prices");

            migrationBuilder.DropTable(
                name: "supplier_margins");

            migrationBuilder.DropColumn(
                name: "source",
                table: "settlement_lines");

            migrationBuilder.DropColumn(
                name: "category",
                table: "prices");

            migrationBuilder.AlterColumn<Guid>(
                name: "pris_id",
                table: "settlement_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
