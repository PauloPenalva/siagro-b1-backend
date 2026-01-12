using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoicesAddColumnSalesInvoicesOriginKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceOriginKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesInvoiceItemOriginKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_SalesInvoiceOriginKey",
                table: "SALES_INVOICES",
                column: "SalesInvoiceOriginKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_SALES_INVOICES_SalesInvoiceOriginKey",
                table: "SALES_INVOICES",
                column: "SalesInvoiceOriginKey",
                principalTable: "SALES_INVOICES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesInvoiceItemOriginKey",
                principalTable: "SALES_INVOICES_ITEMS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_SALES_INVOICES_SalesInvoiceOriginKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_SalesInvoiceOriginKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceItemOriginKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceOriginKey",
                table: "SALES_INVOICES");
        }
    }
}
