using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterDocumentAddBranchFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_BranchCode",
                table: "WEIGHING_TICKETS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_BranchCode",
                table: "STORAGE_TRANSACTIONS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_BranchCode",
                table: "STORAGE_ADDRESSES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_BranchCode",
                table: "SHIPMENT_RELEASES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_BranchCode",
                table: "SALES_INVOICES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_BranchCode",
                table: "SALES_CONTRACTS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_BranchCode",
                table: "PURCHASE_CONTRACTS",
                column: "BranchCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchCode",
                table: "PURCHASE_CONTRACTS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_BRANCHS_BranchCode",
                table: "SALES_CONTRACTS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_BRANCHS_BranchCode",
                table: "SALES_INVOICES",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                table: "SHIPMENT_RELEASES",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_ADDRESSES_BRANCHS_BranchCode",
                table: "STORAGE_ADDRESSES",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_BRANCHS_BranchCode",
                table: "STORAGE_TRANSACTIONS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_BRANCHS_BranchCode",
                table: "WEIGHING_TICKETS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_BRANCHS_BranchCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_BRANCHS_BranchCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_ADDRESSES_BRANCHS_BranchCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_BRANCHS_BranchCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_BRANCHS_BranchCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_BranchCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_BranchCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_BranchCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_BranchCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_BranchCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_BranchCode",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
