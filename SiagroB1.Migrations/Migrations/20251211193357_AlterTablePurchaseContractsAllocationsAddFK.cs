using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAllocationsAddFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.DropForeignKey(
                name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_Code",
                table: "WEIGHING_TICKETS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(15)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_Code",
                table: "WEIGHING_TICKETS",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_DocTypeCode",
                table: "WEIGHING_TICKETS",
                column: "DocTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_ALLOCATIONS_StorageTransactionKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS",
                column: "StorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceCode",
                principalTable: "PROCESSING_SERVICES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "PURCHASE_CONTRACTS",
                column: "HarvestSeasonCode",
                principalTable: "HARVEST_SEASSONS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_STORAGE_TRANSACTIONS_StorageTransactionKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS",
                column: "StorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "TaxCode",
                principalTable: "TAXES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "QUALITY_INSPECTIONS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "SALES_CONTRACTS",
                column: "HarvestSeasonCode",
                principalTable: "HARVEST_SEASSONS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");

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

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_TYPES_DocTypeCode",
                table: "WEIGHING_TICKETS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS",
                column: "TruckCode",
                principalTable: "TRUCKS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS",
                column: "TruckDriverCode",
                principalTable: "TRUCK_DRIVERS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_STORAGE_TRANSACTIONS_StorageTransactionKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.DropForeignKey(
                name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_TYPES_DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_Code",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_ALLOCATIONS_StorageTransactionKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(15)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_Code",
                table: "WEIGHING_TICKETS",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceCode",
                principalTable: "PROCESSING_SERVICES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "PURCHASE_CONTRACTS",
                column: "HarvestSeasonCode",
                principalTable: "HARVEST_SEASSONS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "TaxCode",
                principalTable: "TAXES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "QUALITY_INSPECTIONS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                table: "SALES_CONTRACTS",
                column: "HarvestSeasonCode",
                principalTable: "HARVEST_SEASSONS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                column: "WarehouseCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                column: "QualityAttribCode",
                principalTable: "QUALITY_ATTRIBS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS",
                column: "TruckCode",
                principalTable: "TRUCKS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS",
                column: "TruckDriverCode",
                principalTable: "TRUCK_DRIVERS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
