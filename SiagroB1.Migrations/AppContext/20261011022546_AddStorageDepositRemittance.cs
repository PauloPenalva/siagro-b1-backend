using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddStorageDepositRemittance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StorageDepositRole",
                table: "USAGE_EFFECTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentReleaseKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "STORAGE_RETURN_RECONCILIATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_RETURN_RECONCILIATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_RETURN_RECONCILIATIONS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_STORAGE_RETURN_RECONCILIATIONS_STORAGE_TRANSACTIONS_StorageTransactionKey",
                        column: x => x.StorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ShipmentReleaseKey",
                table: "SALES_INVOICES",
                column: "ShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_RETURN_RECONCILIATIONS_PurchaseInvoiceKey",
                table: "STORAGE_RETURN_RECONCILIATIONS",
                column: "PurchaseInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_RETURN_RECONCILIATIONS_StorageTransactionKey",
                table: "STORAGE_RETURN_RECONCILIATIONS",
                column: "StorageTransactionKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "SALES_INVOICES",
                column: "ShipmentReleaseKey",
                principalTable: "SHIPMENT_RELEASES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropTable(
                name: "STORAGE_RETURN_RECONCILIATIONS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ShipmentReleaseKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "StorageDepositRole",
                table: "USAGE_EFFECTS");

            migrationBuilder.DropColumn(
                name: "ShipmentReleaseKey",
                table: "SALES_INVOICES");
        }
    }
}
