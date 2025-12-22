using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_TYPES_DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocNumberKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_NUMBERS_DocNumberKey",
                table: "STORAGE_TRANSACTIONS",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_NUMBERS_DocNumberKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocNumberKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_TYPES_DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
