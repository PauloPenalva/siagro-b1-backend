using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddStorageCustodyFiscalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StorageCustodyRole",
                table: "USAGE_EFFECTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresProcessing",
                table: "STORAGE_ADDRESSES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CustodyRole",
                table: "SALES_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodyStorageTransactionKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustodyRole",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_CustodyStorageTransactionKey",
                table: "SALES_INVOICES",
                column: "CustodyStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES",
                column: "CustodyStorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_INVOICES_STORAGE_TRANSACTIONS_CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES",
                column: "CustodyStorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_TRANSACTIONS_CustodyStorageTransactionKey",
                table: "SALES_INVOICES",
                column: "CustodyStorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_INVOICES_STORAGE_TRANSACTIONS_CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_STORAGE_TRANSACTIONS_CustodyStorageTransactionKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_CustodyStorageTransactionKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_INVOICES_CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "StorageCustodyRole",
                table: "USAGE_EFFECTS");

            migrationBuilder.DropColumn(
                name: "RequiresProcessing",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "CustodyRole",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CustodyStorageTransactionKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CustodyRole",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "CustodyStorageTransactionKey",
                table: "PURCHASE_INVOICES");
        }
    }
}
