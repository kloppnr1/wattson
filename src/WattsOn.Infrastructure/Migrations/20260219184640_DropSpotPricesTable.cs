using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropSpotPricesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spot_prices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spot_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HourDk = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HourUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceArea = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SpotPriceDkkPerMwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    SpotPriceEurPerMwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spot_prices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_spot_prices_HourUtc_PriceArea",
                table: "spot_prices",
                columns: new[] { "HourUtc", "PriceArea" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spot_prices_PriceArea_HourUtc",
                table: "spot_prices",
                columns: new[] { "PriceArea", "HourUtc" });
        }
    }
}
