using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadPlannedContractRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                column: "SalesShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ShipmentLoadKey_ContractKey_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey", "SalesShipmentReleaseKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentLoadKey_ContractKey_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey", "ShipmentReleaseKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                column: "ShipmentReleaseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                column: "ShipmentReleaseKey",
                principalTable: "SHIPMENT_RELEASES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                column: "SalesShipmentReleaseKey",
                principalTable: "SALES_SHIPMENT_RELEASES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ShipmentLoadKey_ContractKey_SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentLoadKey_ContractKey_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "SalesShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "ShipmentReleaseKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey" },
                unique: true);
        }
    }
}
