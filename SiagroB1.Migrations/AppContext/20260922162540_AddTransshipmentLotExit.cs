using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTransshipmentLotExit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_TRANSSHIPMENTS_LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                column: "LotExitStorageTransactionKey",
                unique: true,
                filter: "[LotExitStorageTransactionKey] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOAD_TRANSSHIPMENTS_STORAGE_TRANSACTIONS_LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                column: "LotExitStorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOAD_TRANSSHIPMENTS_STORAGE_TRANSACTIONS_LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOAD_TRANSSHIPMENTS_LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");

            migrationBuilder.DropColumn(
                name: "LotExitStorageTransactionKey",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");
        }
    }
}
