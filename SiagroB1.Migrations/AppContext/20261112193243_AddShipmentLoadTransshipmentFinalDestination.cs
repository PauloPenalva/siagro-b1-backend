using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadTransshipmentFinalDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInTransfer",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalDestinationAt",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalDestinationBy",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInTransfer",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "FinalDestinationAt",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");

            migrationBuilder.DropColumn(
                name: "FinalDestinationBy",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "SHIPMENT_LOAD_TRANSSHIPMENTS");
        }
    }
}
