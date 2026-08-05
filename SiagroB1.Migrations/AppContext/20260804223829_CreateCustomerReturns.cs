using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateCustomerReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CUSTOMER_RETURNS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    DocumentNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    DocumentSeries = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxPayerComments = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    XmlFileName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    XmlData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_RETURNS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMER_RETURNS_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerReturnKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_RETURNS_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_CUSTOMER_RETURNS_ITEMS_CUSTOMER_RETURNS_CustomerReturnKey",
                        column: x => x.CustomerReturnKey,
                        principalTable: "CUSTOMER_RETURNS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_CUSTOMER_RETURNS_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_AccessKey",
                table: "CUSTOMER_RETURNS",
                column: "AccessKey",
                unique: true,
                filter: "[AccessKey] IS NOT NULL AND [Status] <> 1");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_ITEMS_CustomerReturnKey",
                table: "CUSTOMER_RETURNS_ITEMS",
                column: "CustomerReturnKey");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_ITEMS_SalesInvoiceItemKey",
                table: "CUSTOMER_RETURNS_ITEMS",
                column: "SalesInvoiceItemKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CUSTOMER_RETURNS_ITEMS");

            migrationBuilder.DropTable(
                name: "CUSTOMER_RETURNS");
        }
    }
}
