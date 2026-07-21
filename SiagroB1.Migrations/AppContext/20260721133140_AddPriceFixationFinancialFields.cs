using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPriceFixationFinancialFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinancialDueDate",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentDetails",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(1000)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinancialDueDate",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "PaymentDetails",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");
        }
    }
}
