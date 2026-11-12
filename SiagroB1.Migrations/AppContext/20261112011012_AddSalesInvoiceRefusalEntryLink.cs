using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceRefusalEntryLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RefusalStorageTransactionKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_RefusalStorageTransactionKey",
                table: "SALES_INVOICES",
                column: "RefusalStorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_TRANSACTIONS_RefusalStorageTransactionKey",
                table: "SALES_INVOICES",
                column: "RefusalStorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_TRANSACTIONS_RefusalStorageTransactionKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_RefusalStorageTransactionKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "RefusalStorageTransactionKey",
                table: "SALES_INVOICES");
        }
    }
}
