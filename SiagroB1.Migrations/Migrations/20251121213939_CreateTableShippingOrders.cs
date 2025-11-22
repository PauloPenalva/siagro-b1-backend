using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableShippingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.RenameColumn(
                name: "Cpf",
                table: "TRUCK_DRIVERS",
                newName: "Code");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SALES_INVOICES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    InvoiceSeries = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SHIPPING_ORDERS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    TruckCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    TruckDriver = table.Column<string>(type: "VARCHAR(11)", nullable: false),
                    StorageAddressKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Volume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPPING_ORDERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                        column: x => x.StorageAddressKey,
                        principalTable: "STORAGE_ADDRESSES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SALES_INVOICES_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    SalesContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICES_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICES_ITEMS_SALES_CONTRACTS_SalesContractId",
                        column: x => x.SalesContractId,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SALES_INVOICES_ITEMS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesContractId",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesInvoiceKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ORDERS_StorageAddressKey",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropTable(
                name: "SHIPPING_ORDERS");

            migrationBuilder.DropTable(
                name: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "SALES_CONTRACTS");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "TRUCK_DRIVERS",
                newName: "Cpf");

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FixationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FixationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    FixationVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_PRICE_FIXATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_PRICE_FIXATIONS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_PRICE_FIXATIONS_SalesContractKey",
                table: "SALES_CONTRACTS_PRICE_FIXATIONS",
                column: "SalesContractKey");
        }
    }
}
