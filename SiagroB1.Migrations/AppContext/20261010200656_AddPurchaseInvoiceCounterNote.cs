using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceCounterNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsCounterNote",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProducerInvoiceKey",
                table: "PURCHASE_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "ProducerInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ProducerInvoiceKey",
                table: "PURCHASE_INVOICES",
                column: "ProducerInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_INVOICES_PURCHASE_INVOICES_ProducerInvoiceKey",
                table: "PURCHASE_INVOICES",
                column: "ProducerInvoiceKey",
                principalTable: "PURCHASE_INVOICES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_INVOICES_ITEMS_ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "ProducerInvoiceItemKey",
                principalTable: "PURCHASE_INVOICES_ITEMS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_INVOICES_PURCHASE_INVOICES_ProducerInvoiceKey",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_INVOICES_ITEMS_ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_INVOICES_ProducerInvoiceKey",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "AllowsCounterNote",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "ProducerInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "ProducerInvoiceKey",
                table: "PURCHASE_INVOICES");
        }
    }
}
