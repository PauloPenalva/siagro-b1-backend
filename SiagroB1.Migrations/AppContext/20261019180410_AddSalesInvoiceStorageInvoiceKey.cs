using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceStorageInvoiceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StorageInvoiceKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_StorageInvoiceKey",
                table: "SALES_INVOICES",
                column: "StorageInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_INVOICES_StorageInvoiceKey",
                table: "SALES_INVOICES",
                column: "StorageInvoiceKey",
                principalTable: "STORAGE_INVOICES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_INVOICES_StorageInvoiceKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_StorageInvoiceKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "StorageInvoiceKey",
                table: "SALES_INVOICES");
        }
    }
}
