using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveSupplierIdentityToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplies_supplier_identities_supplier_identity_id",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "IX_supplies_supplier_identity_id",
                table: "supplies");

            migrationBuilder.DropColumn(
                name: "supplier_identity_id",
                table: "supplies");

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_identity_id",
                table: "customers",
                type: "uuid",
                nullable: true);

            // Backfill: assign all existing customers to the first active supplier identity
            migrationBuilder.Sql("""
                UPDATE customers
                SET supplier_identity_id = (
                    SELECT id FROM supplier_identities WHERE is_active = true ORDER BY created_at LIMIT 1
                )
                WHERE supplier_identity_id IS NULL;
            """);

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_identity_id",
                table: "customers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_customers_supplier_identity_id",
                table: "customers",
                column: "supplier_identity_id");

            migrationBuilder.AddForeignKey(
                name: "FK_customers_supplier_identities_supplier_identity_id",
                table: "customers",
                column: "supplier_identity_id",
                principalTable: "supplier_identities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customers_supplier_identities_supplier_identity_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "IX_customers_supplier_identity_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "supplier_identity_id",
                table: "customers");

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_identity_id",
                table: "supplies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_supplies_supplier_identity_id",
                table: "supplies",
                column: "supplier_identity_id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplies_supplier_identities_supplier_identity_id",
                table: "supplies",
                column: "supplier_identity_id",
                principalTable: "supplier_identities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
