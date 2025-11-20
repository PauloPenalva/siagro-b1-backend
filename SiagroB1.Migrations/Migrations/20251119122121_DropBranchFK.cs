using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DropBranchFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPPING_MANIFESTS_BRANCHS_BranchCode",
                table: "SHIPPING_MANIFESTS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_BRANCHS_BranchCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHTING_TICKETS_BranchCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_LOTS_BranchCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropIndex(
                name: "IX_SHIPPING_MANIFESTS_BranchCode",
                table: "SHIPPING_MANIFESTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_BranchCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_BranchCode",
                table: "WEIGHTING_TICKETS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_BranchCode",
                table: "STORAGE_LOTS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_MANIFESTS_BranchCode",
                table: "SHIPPING_MANIFESTS",
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
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPPING_MANIFESTS_BRANCHS_BranchCode",
                table: "SHIPPING_MANIFESTS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_BRANCHS_BranchCode",
                table: "STORAGE_LOTS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchCode",
                table: "WEIGHTING_TICKETS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
