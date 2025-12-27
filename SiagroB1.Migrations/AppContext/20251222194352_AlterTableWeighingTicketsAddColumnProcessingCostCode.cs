#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableWeighingTicketsAddColumnProcessingCostCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageAddressKey",
                table: "WEIGHING_TICKETS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressKey",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressKey");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "StorageAddressKey",
                table: "WEIGHING_TICKETS");
        }
    }
}
