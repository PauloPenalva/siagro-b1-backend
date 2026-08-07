using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceItemContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "PurchaseContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PurchaseContractKey",
                table: "PURCHASE_INVOICES_ITEMS");
        }
    }
}
