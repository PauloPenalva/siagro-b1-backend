using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnSalesInvoiceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                column: "SalesInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                column: "SalesInvoiceKey",
                principalTable: "SALES_INVOICES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
