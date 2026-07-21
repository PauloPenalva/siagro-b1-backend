using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableSalesShipmentReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SALES_SHIPMENT_RELEASES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    DeliveryLocationCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    DeliveryLocationName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancellationReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ShippedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_SHIPMENT_RELEASES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_SALES_SHIPMENT_RELEASES_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SALES_SHIPMENT_RELEASES_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "SalesShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_SHIPMENT_RELEASES_BranchCode",
                table: "SALES_SHIPMENT_RELEASES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_SHIPMENT_RELEASES_DocNumberKey",
                table: "SALES_SHIPMENT_RELEASES",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_SHIPMENT_RELEASES_SalesContractKey",
                table: "SALES_SHIPMENT_RELEASES",
                column: "SalesContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesShipmentReleaseKey",
                principalTable: "SALES_SHIPMENT_RELEASES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "SalesShipmentReleaseKey",
                principalTable: "SALES_SHIPMENT_RELEASES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "SALES_SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "SalesShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "SalesShipmentReleaseKey",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
