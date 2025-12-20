using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoicesItemsAlterColumnSalesContractKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_CONTRACTS_SalesContractId",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.RenameColumn(
                name: "SalesContractId",
                table: "SALES_INVOICES_ITEMS",
                newName: "SalesContractKey");

            migrationBuilder.RenameIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesContractId",
                table: "SALES_INVOICES_ITEMS",
                newName: "IX_SALES_INVOICES_ITEMS_SalesContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_CONTRACTS_SalesContractKey",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesContractKey",
                principalTable: "SALES_CONTRACTS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_CONTRACTS_SalesContractKey",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.RenameColumn(
                name: "SalesContractKey",
                table: "SALES_INVOICES_ITEMS",
                newName: "SalesContractId");

            migrationBuilder.RenameIndex(
                name: "IX_SALES_INVOICES_ITEMS_SalesContractKey",
                table: "SALES_INVOICES_ITEMS",
                newName: "IX_SALES_INVOICES_ITEMS_SalesContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_SALES_CONTRACTS_SalesContractId",
                table: "SALES_INVOICES_ITEMS",
                column: "SalesContractId",
                principalTable: "SALES_CONTRACTS",
                principalColumn: "Key");
        }
    }
}
