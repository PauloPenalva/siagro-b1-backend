using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractsAddColumnPaymentTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StandardCashFlowDate",
                table: "SALES_CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StandardCurrency",
                table: "SALES_CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "StandardCashFlowDate",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "StandardCurrency",
                table: "SALES_CONTRACTS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS",
                column: "Code",
                unique: true);
        }
    }
}
