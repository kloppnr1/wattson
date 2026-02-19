using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    metering_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_series_version = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issue_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_issues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_issues_metering_point_id",
                table: "settlement_issues",
                column: "metering_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_issues_metering_point_id_time_series_id_time_ser~",
                table: "settlement_issues",
                columns: new[] { "metering_point_id", "time_series_id", "time_series_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_issues_status",
                table: "settlement_issues",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_issues");
        }
    }
}
