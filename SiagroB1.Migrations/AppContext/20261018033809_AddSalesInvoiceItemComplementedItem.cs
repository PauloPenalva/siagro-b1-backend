using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceItemComplementedItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS",
                column: "ComplementedItemKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_INVOICES_ITEMS_ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS",
                column: "ComplementedItemKey",
                principalTable: "SALES_INVOICES_ITEMS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_INVOICES_ITEMS_ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ITEMS_ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "ComplementedItemKey",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
