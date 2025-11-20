using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
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
