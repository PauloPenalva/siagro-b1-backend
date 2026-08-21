using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateShipmentLoads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentLoadKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOADS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    LoadDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    TruckCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    TruckDriverCode = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    WarehouseName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    TotalQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    InvoicedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CancellationReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
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
                    table.PrimaryKey("PK_SHIPMENT_LOADS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_MOVEMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    Description = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_MOVEMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_MOVEMENTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ShipmentLoadKey",
                table: "SALES_INVOICES",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_MOVEMENTS_ShipmentLoadKey",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_BranchCode",
                table: "SHIPMENT_LOADS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_Code",
                table: "SHIPMENT_LOADS",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_DocNumberKey",
                table: "SHIPMENT_LOADS",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_LOADS_ShipmentLoadKey",
                table: "SALES_INVOICES",
                column: "ShipmentLoadKey",
                principalTable: "SHIPMENT_LOADS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOADS_ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentLoadKey",
                principalTable: "SHIPMENT_LOADS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_LOADS_ShipmentLoadKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOADS_ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOADS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ShipmentLoadKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "ShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ShipmentLoadKey",
                table: "SALES_INVOICES");
        }
    }
}
