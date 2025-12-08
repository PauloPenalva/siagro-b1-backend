using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnInvoiceNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(9)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSerie",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "InvoiceSerie",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
