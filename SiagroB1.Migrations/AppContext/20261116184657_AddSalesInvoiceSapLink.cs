using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceSapLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SapCancelDetectedAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SapCancelPending",
                table: "SALES_INVOICES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SapCancelPendingReason",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SapDocEntry",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SapDocNum",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_SapDocEntry",
                table: "SALES_INVOICES",
                column: "SapDocEntry",
                unique: true,
                filter: "[SapDocEntry] IS NOT NULL AND [InvoiceStatus] <> 2 AND [InvoiceStatus] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_SapDocEntry",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapCancelDetectedAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapCancelPending",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapCancelPendingReason",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapDocEntry",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapDocNum",
                table: "SALES_INVOICES");
        }
    }
}
