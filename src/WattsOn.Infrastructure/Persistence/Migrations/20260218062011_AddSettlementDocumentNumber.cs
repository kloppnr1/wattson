using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementDocumentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS settlement_document_seq START WITH 1 INCREMENT BY 1;");

            migrationBuilder.AddColumn<long>(
                name: "document_number",
                table: "afregninger",
                type: "bigint",
                nullable: false,
                defaultValueSql: "nextval('settlement_document_seq')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "document_number",
                table: "afregninger");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS settlement_document_seq;");
        }
    }
}
