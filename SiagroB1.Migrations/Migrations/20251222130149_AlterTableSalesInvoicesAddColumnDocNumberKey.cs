using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoicesAddColumnDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_DOC_TYPES_DocTypeCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_DocTypeCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "InvoiceSeries",
                table: "SALES_INVOICES");

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_DocNumberKey",
                table: "SALES_INVOICES",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_DOC_NUMBERS_DocNumberKey",
                table: "SALES_INVOICES",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_DOC_NUMBERS_DocNumberKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_DocNumberKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "SALES_INVOICES");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSeries",
                table: "SALES_INVOICES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_DocTypeCode",
                table: "SALES_INVOICES",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_DOC_TYPES_DocTypeCode",
                table: "SALES_INVOICES",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
