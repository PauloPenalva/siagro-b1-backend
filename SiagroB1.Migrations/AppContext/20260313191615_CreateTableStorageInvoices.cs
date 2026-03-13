using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableStorageInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InvoicedAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiced",
                table: "STORAGE_TRANSACTIONS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageInvoiceKey",
                table: "STORAGE_CHARGES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "STORAGE_INVOICES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    StorageAddressCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TotalQuantityLoss = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_INVOICES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_INVOICES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_STORAGE_INVOICES_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "STORAGE_INVOICE_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    ReferenceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnitPriceOrRate = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TotalQuantityLoss = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    SourceType = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    SourceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCode = table.Column<string>(type: "VARCHAR(50)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_INVOICE_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_INVOICE_ITEMS_STORAGE_INVOICES_StorageInvoiceKey",
                        column: x => x.StorageInvoiceKey,
                        principalTable: "STORAGE_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICE_ITEMS_StorageInvoiceKey",
                table: "STORAGE_INVOICE_ITEMS",
                column: "StorageInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_BranchCode",
                table: "STORAGE_INVOICES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_Code",
                table: "STORAGE_INVOICES",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_DocNumberKey",
                table: "STORAGE_INVOICES",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_StorageAddressCode_PeriodStart_PeriodEnd",
                table: "STORAGE_INVOICES",
                columns: new[] { "StorageAddressCode", "PeriodStart", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STORAGE_INVOICE_ITEMS");

            migrationBuilder.DropTable(
                name: "STORAGE_INVOICES");

            migrationBuilder.DropColumn(
                name: "InvoicedAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "IsInvoiced",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "StorageInvoiceKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "StorageInvoiceKey",
                table: "STORAGE_CHARGES");
        }
    }
}
