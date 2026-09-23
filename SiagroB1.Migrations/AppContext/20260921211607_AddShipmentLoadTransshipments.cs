using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadTransshipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransshippedQuantity",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    WarehouseName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    TransshipmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OutgoingQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    EntryQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    EntryStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_TRANSSHIPMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_TRANSSHIPMENTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_TRANSSHIPMENTS_STORAGE_TRANSACTIONS_EntryStorageTransactionKey",
                        column: x => x.EntryStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentLoadTransshipmentKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_TRANSSHIPMENTS_EntryStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                column: "EntryStorageTransactionKey",
                unique: true,
                filter: "[EntryStorageTransactionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_TRANSSHIPMENTS_ShipmentLoadKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                column: "ShipmentLoadKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOAD_TRANSSHIPMENTS_ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentLoadTransshipmentKey",
                principalTable: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOAD_TRANSSHIPMENTS_ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_TRANSSHIPMENTS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ShipmentLoadTransshipmentKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "TransshippedQuantity",
                table: "SHIPMENT_LOADS");
        }
    }
}
