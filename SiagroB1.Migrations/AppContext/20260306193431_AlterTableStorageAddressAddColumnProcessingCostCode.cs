using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageAddressAddColumnProcessingCostCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_ProcessingCostCode",
                table: "STORAGE_ADDRESSES",
                column: "ProcessingCostCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_ADDRESSES_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_ADDRESSES",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_ADDRESSES_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_ProcessingCostCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "STORAGE_ADDRESSES");
        }
    }
}
