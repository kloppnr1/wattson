using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFakturaAddAfregningStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faktura_afregninger");

            migrationBuilder.DropTable(
                name: "faktura_linjer");

            migrationBuilder.DropTable(
                name: "fakturaer");

            migrationBuilder.AddColumn<string>(
                name: "external_invoice_reference",
                table: "afregninger",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invoiced_at",
                table: "afregninger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "afregninger",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_afregninger_status",
                table: "afregninger",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_afregninger_status",
                table: "afregninger");

            migrationBuilder.DropColumn(
                name: "external_invoice_reference",
                table: "afregninger");

            migrationBuilder.DropColumn(
                name: "invoiced_at",
                table: "afregninger");

            migrationBuilder.DropColumn(
                name: "status",
                table: "afregninger");

            migrationBuilder.CreateTable(
                name: "fakturaer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kunde_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_faktura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    is_sent = table.Column<bool>(type: "boolean", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoice_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoice_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sub_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sub_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fakturaer", x => x.id);
                    table.ForeignKey(
                        name: "FK_fakturaer_fakturaer_original_faktura_id",
                        column: x => x.original_faktura_id,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fakturaer_kunder_kunde_id",
                        column: x => x.kunde_id,
                        principalTable: "kunder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faktura_afregninger",
                columns: table => new
                {
                    AfregningerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktura_afregninger", x => new { x.AfregningerId, x.FakturaId });
                    table.ForeignKey(
                        name: "FK_faktura_afregninger_afregninger_AfregningerId",
                        column: x => x.AfregningerId,
                        principalTable: "afregninger",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_faktura_afregninger_fakturaer_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "faktura_linjer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    afregning_linje_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    faktura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    quantity_unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true, defaultValue: "kWh"),
                    quantity_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktura_linjer", x => x.id);
                    table.ForeignKey(
                        name: "FK_faktura_linjer_fakturaer_faktura_id",
                        column: x => x.faktura_id,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_faktura_afregninger_FakturaId",
                table: "faktura_afregninger",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_faktura_linjer_faktura_id",
                table: "faktura_linjer",
                column: "faktura_id");

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_invoice_number",
                table: "fakturaer",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_kunde_id",
                table: "fakturaer",
                column: "kunde_id");

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_original_faktura_id",
                table: "fakturaer",
                column: "original_faktura_id");
        }
    }
}
