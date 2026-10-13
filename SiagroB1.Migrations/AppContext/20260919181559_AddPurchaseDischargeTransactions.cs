using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseDischargeTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DischargeSalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DischargeShippingTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_ITEMS_DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DischargeSalesInvoiceItemKey",
                principalTable: "SALES_INVOICES_ITEMS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_TRANSACTIONS_DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DischargeShippingTransactionKey",
                principalTable: "SHIPPING_TRANSACTIONS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_ITEMS_DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_TRANSACTIONS_DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DischargeSalesInvoiceItemKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DischargeShippingTransactionKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
