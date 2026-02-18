using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertObservationsToHypertable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_observations",
                table: "observations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_observations",
                table: "observations",
                columns: new[] { "timestamp", "id" });

            // Convert to TimescaleDB hypertable for efficient time-range queries
            migrationBuilder.Sql(
                "SELECT create_hypertable('observations', 'timestamp', migrate_data => true);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_observations",
                table: "observations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_observations",
                table: "observations",
                column: "id");
        }
    }
}
