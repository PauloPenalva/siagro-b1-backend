#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class RenameBranchKeyToBranchCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchKey",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_BRANCHS_BranchKey",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchKey",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.RenameColumn(
                name: "BranchKey",
                table: "WEIGHTING_TICKETS",
                newName: "BranchCode");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHTING_TICKETS_BranchKey",
                table: "WEIGHTING_TICKETS",
                newName: "IX_WEIGHTING_TICKETS_BranchCode");

            migrationBuilder.RenameColumn(
                name: "BranchKey",
                table: "STORAGE_LOTS",
                newName: "BranchCode");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_LOTS_BranchKey",
                table: "STORAGE_LOTS",
                newName: "IX_STORAGE_LOTS_BranchCode");

            migrationBuilder.RenameColumn(
                name: "BranchKey",
                table: "PURCHASE_CONTRACTS",
                newName: "BranchCode");

            migrationBuilder.RenameIndex(
                name: "IX_PURCHASE_CONTRACTS_BranchKey",
                table: "PURCHASE_CONTRACTS",
                newName: "IX_PURCHASE_CONTRACTS_BranchCode");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "BRANCHS",
                newName: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchCode",
                table: "PURCHASE_CONTRACTS",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_BRANCHS_BranchCode",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.RenameColumn(
                name: "BranchCode",
                table: "WEIGHTING_TICKETS",
                newName: "BranchKey");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHTING_TICKETS_BranchCode",
                table: "WEIGHTING_TICKETS",
                newName: "IX_WEIGHTING_TICKETS_BranchKey");

            migrationBuilder.RenameColumn(
                name: "BranchCode",
                table: "STORAGE_LOTS",
                newName: "BranchKey");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_LOTS_BranchCode",
                table: "STORAGE_LOTS",
                newName: "IX_STORAGE_LOTS_BranchKey");

            migrationBuilder.RenameColumn(
                name: "BranchCode",
                table: "PURCHASE_CONTRACTS",
                newName: "BranchKey");

            migrationBuilder.RenameIndex(
                name: "IX_PURCHASE_CONTRACTS_BranchCode",
                table: "PURCHASE_CONTRACTS",
                newName: "IX_PURCHASE_CONTRACTS_BranchKey");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "BRANCHS",
                newName: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchKey",
                table: "PURCHASE_CONTRACTS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_BRANCHS_BranchKey",
                table: "STORAGE_LOTS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchKey",
                table: "WEIGHTING_TICKETS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
