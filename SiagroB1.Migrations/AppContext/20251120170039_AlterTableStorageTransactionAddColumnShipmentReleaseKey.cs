#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionAddColumnShipmentReleaseKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipmentReleaseKey",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentReleaseKey",
                table: "STORAGE_ADDRESSES",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
