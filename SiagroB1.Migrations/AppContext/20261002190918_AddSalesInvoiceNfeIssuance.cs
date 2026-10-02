using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceNfeIssuance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NfeAuthorizedAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeConfirmationError",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeEnvironment",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeProtocol",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeRandomCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeStatus",
                table: "SALES_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NfeStatusCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeStatusReason",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SALES_INVOICE_NFE_XMLS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Xml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICE_NFE_XMLS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICE_NFE_XMLS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICE_NFE_XMLS_SalesInvoiceKey",
                table: "SALES_INVOICE_NFE_XMLS",
                column: "SalesInvoiceKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_INVOICE_NFE_XMLS");

            migrationBuilder.DropColumn(
                name: "NfeAuthorizedAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeConfirmationError",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeEnvironment",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeProtocol",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeRandomCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatus",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatusCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatusReason",
                table: "SALES_INVOICES");
        }
    }
}
