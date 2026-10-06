using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddNfeCorrectionLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICE_NFE_CORRECTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "NVARCHAR(1000)", nullable: false),
                    Protocol = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(255)", nullable: false),
                    ProcEventXml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICE_NFE_CORRECTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICE_NFE_CORRECTIONS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_INVOICE_NFE_CORRECTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "NVARCHAR(1000)", nullable: false),
                    Protocol = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(255)", nullable: false),
                    ProcEventXml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICE_NFE_CORRECTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICE_NFE_CORRECTIONS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICE_NFE_CORRECTIONS_PurchaseInvoiceKey_Sequence",
                table: "PURCHASE_INVOICE_NFE_CORRECTIONS",
                columns: new[] { "PurchaseInvoiceKey", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICE_NFE_CORRECTIONS_SalesInvoiceKey_Sequence",
                table: "SALES_INVOICE_NFE_CORRECTIONS",
                columns: new[] { "SalesInvoiceKey", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICE_NFE_CORRECTIONS");

            migrationBuilder.DropTable(
                name: "SALES_INVOICE_NFE_CORRECTIONS");
        }
    }
}
