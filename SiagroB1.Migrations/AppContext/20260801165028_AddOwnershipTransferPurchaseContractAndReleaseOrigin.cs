using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddOwnershipTransferPurchaseContractAndReleaseOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "SHIPMENT_RELEASES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnershipTransferKey",
                table: "SHIPMENT_RELEASES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAddressCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseContractCode",
                table: "OWNERSHIP_TRANSFER",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_OwnershipTransferKey",
                table: "SHIPMENT_RELEASES",
                column: "OwnershipTransferKey",
                unique: true,
                filter: "[OwnershipTransferKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_StorageAddressCode",
                table: "SHIPMENT_RELEASES",
                column: "StorageAddressCode");

            migrationBuilder.CreateIndex(
                name: "IX_OWNERSHIP_TRANSFER_PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER",
                column: "PurchaseContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_OWNERSHIP_TRANSFER_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_OWNERSHIP_TRANSFER_OwnershipTransferKey",
                table: "SHIPMENT_RELEASES",
                column: "OwnershipTransferKey",
                principalTable: "OWNERSHIP_TRANSFER",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_STORAGE_ADDRESSES_StorageAddressCode",
                table: "SHIPMENT_RELEASES",
                column: "StorageAddressCode",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OWNERSHIP_TRANSFER_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_OWNERSHIP_TRANSFER_OwnershipTransferKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_STORAGE_ADDRESSES_StorageAddressCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_OwnershipTransferKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_StorageAddressCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_OWNERSHIP_TRANSFER_PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "OwnershipTransferKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "StorageAddressCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "PurchaseContractCode",
                table: "OWNERSHIP_TRANSFER");

            migrationBuilder.DropColumn(
                name: "PurchaseContractKey",
                table: "OWNERSHIP_TRANSFER");
        }
    }
}
