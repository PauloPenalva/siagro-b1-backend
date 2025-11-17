using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "STORAGE_LOTS");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "STORAGE_LOTS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_ProcessingCostCode",
                table: "STORAGE_LOTS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_WarehouseCode",
                table: "STORAGE_LOTS",
                column: "WarehouseCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_LOTS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_LOTS",
                column: "WarehouseCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_LOTS_ProcessingCostCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_LOTS_WarehouseCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "STORAGE_LOTS");

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "STORAGE_LOTS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
