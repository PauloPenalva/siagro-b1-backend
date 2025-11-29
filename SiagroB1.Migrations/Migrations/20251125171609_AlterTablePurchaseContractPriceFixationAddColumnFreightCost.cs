using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractPriceFixationAddColumnFreightCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "FreightCost",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<decimal>(
                name: "LossValue",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightCost",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreightCost",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.AlterColumn<decimal>(
                name: "LossValue",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                type: "DECIMAL(18,3)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)");

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightCost",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
