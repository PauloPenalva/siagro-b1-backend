#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableShipmentReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_PurchaseContractKey",
                table: "SHIPMENT_RELEASES",
                column: "PurchaseContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_PurchaseContractKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_BranchCode",
                table: "SHIPMENT_RELEASES",
                column: "BranchCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                table: "SHIPMENT_RELEASES",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
