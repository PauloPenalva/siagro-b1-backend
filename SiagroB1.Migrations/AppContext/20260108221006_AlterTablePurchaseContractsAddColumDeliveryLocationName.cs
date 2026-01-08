using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumDeliveryLocationName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BROKERS_UNITS_OF_MEASURE_ComissionUmCode",
                table: "PURCHASE_CONTRACTS_BROKERS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_WAREHOUSES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_WarehouseCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ITEMS_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_UnitOfMeasureCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_BROKERS_ComissionUmCode",
                table: "PURCHASE_CONTRACTS_BROKERS");

            migrationBuilder.DropColumn(
                name: "Inactive",
                table: "WAREHOUSES");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WAREHOUSES");

            migrationBuilder.AddColumn<string>(
                name: "WarehouseName",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocationName",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(200)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocationName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseName",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationName",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationName",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<bool>(
                name: "Inactive",
                table: "WAREHOUSES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "WAREHOUSES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                column: "UnitOfMeasureCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                column: "WarehouseCode");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES",
                column: "DeliveryLocationCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS",
                column: "UnitOfMeasureCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_UnitOfMeasureCode",
                table: "SALES_CONTRACTS",
                column: "UnitOfMeasureCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_BROKERS_ComissionUmCode",
                table: "PURCHASE_CONTRACTS_BROKERS",
                column: "ComissionUmCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BROKERS_UNITS_OF_MEASURE_ComissionUmCode",
                table: "PURCHASE_CONTRACTS_BROKERS",
                column: "ComissionUmCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_WAREHOUSES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                column: "WarehouseCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code");
        }
    }
}
