using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceDivergences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceDivergenceQuantity",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SapLinkType",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SALES_INVOICE_DIVERGENCES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkType = table.Column<int>(type: "int", nullable: false),
                    EffectiveQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    InvoiceQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    QuantityDifference = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    EffectiveValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    InvoiceValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ValueDifference = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    VoidReason = table.Column<string>(type: "NVARCHAR(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICE_DIVERGENCES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICE_DIVERGENCES_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICE_DIVERGENCES_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICE_DIVERGENCES_SalesInvoiceKey",
                table: "SALES_INVOICE_DIVERGENCES",
                column: "SalesInvoiceKey",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICE_DIVERGENCES_ShipmentLoadKey",
                table: "SALES_INVOICE_DIVERGENCES",
                column: "ShipmentLoadKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_INVOICE_DIVERGENCES");

            migrationBuilder.DropColumn(
                name: "InvoiceDivergenceQuantity",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "SapLinkType",
                table: "SALES_INVOICES");
        }
    }
}
