using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableShipmentReleasesAddColumnKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SHIPMENT_RELEASES",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "SHIPMENT_RELEASES",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_SHIPMENT_RELEASES",
                table: "SHIPMENT_RELEASES",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseKey",
                principalTable: "SHIPMENT_RELEASES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SHIPMENT_RELEASES",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.AddColumn<int>(
                name: "ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SHIPMENT_RELEASES",
                table: "SHIPMENT_RELEASES",
                column: "RowId");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseRowId");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseRowId",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseRowId",
                principalTable: "SHIPMENT_RELEASES",
                principalColumn: "RowId");
        }
    }
}
