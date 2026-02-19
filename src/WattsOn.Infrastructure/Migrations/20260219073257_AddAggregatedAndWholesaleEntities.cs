using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregatedAndWholesaleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aggregated_time_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grid_area = table.Column<string>(type: "text", nullable: false),
                    business_reason = table.Column<string>(type: "text", nullable: false),
                    metering_point_type = table.Column<string>(type: "text", nullable: false),
                    settlement_method = table.Column<string>(type: "text", nullable: true),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    total_energy_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quality_status = table.Column<string>(type: "text", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    transaction_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aggregated_time_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wholesale_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grid_area = table.Column<string>(type: "text", nullable: false),
                    business_reason = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_energy_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    total_amount_dkk = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    transaction_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wholesale_settlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aggregated_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregated_time_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aggregated_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_aggregated_observations_aggregated_time_series_aggregated_t~",
                        column: x => x.aggregated_time_series_id,
                        principalTable: "aggregated_time_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wholesale_settlement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wholesale_settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    charge_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    owner_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    energy_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    amount_dkk = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wholesale_settlement_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wholesale_settlement_lines_wholesale_settlements_wholesale_~",
                        column: x => x.wholesale_settlement_id,
                        principalTable: "wholesale_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aggregated_observations_aggregated_time_series_id",
                table: "aggregated_observations",
                column: "aggregated_time_series_id");

            migrationBuilder.CreateIndex(
                name: "IX_wholesale_settlement_lines_wholesale_settlement_id",
                table: "wholesale_settlement_lines",
                column: "wholesale_settlement_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aggregated_observations");

            migrationBuilder.DropTable(
                name: "wholesale_settlement_lines");

            migrationBuilder.DropTable(
                name: "aggregated_time_series");

            migrationBuilder.DropTable(
                name: "wholesale_settlements");
        }
    }
}
