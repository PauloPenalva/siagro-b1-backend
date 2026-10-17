using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddCtePurchaseEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CteIcmsCst",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CteIcmsValue",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(19,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightEndCity",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightEndState",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightPayerTaxId",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightStartCity",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightStartState",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreightServiceItemCode",
                table: "BRANCHS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransportedPurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_PURCHASE_INVOICES_TransportedPurchaseInvoiceKey",
                        column: x => x.TransportedPurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_PurchaseInvoiceKey_AccessKey",
                table: "PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS",
                columns: new[] { "PurchaseInvoiceKey", "AccessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_SalesInvoiceKey",
                table: "PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS",
                column: "SalesInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS_TransportedPurchaseInvoiceKey",
                table: "PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS",
                column: "TransportedPurchaseInvoiceKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "CteIcmsCst",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "CteIcmsValue",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightEndCity",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightEndState",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightPayerTaxId",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightStartCity",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightStartState",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightServiceItemCode",
                table: "BRANCHS");
        }
    }
}
