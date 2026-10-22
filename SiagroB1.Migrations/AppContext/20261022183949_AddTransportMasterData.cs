using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTransportMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BodyType",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapacityKg",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapacityM3",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Inactive",
                table: "TRUCKS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "TRUCKS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OwnerCardCode",
                table: "TRUCKS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerKind",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerRntrc",
                table: "TRUCKS",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerType",
                table: "TRUCKS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Renavam",
                table: "TRUCKS",
                type: "VARCHAR(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WheelType",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Inactive",
                table: "TRUCK_DRIVERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LicenseCategory",
                table: "TRUCK_DRIVERS",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseExpiresOn",
                table: "TRUCK_DRIVERS",
                type: "DATE",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNumber",
                table: "TRUCK_DRIVERS",
                type: "VARCHAR(11)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyType",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "CapacityKg",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "CapacityM3",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "Inactive",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "OwnerCardCode",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "OwnerKind",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "OwnerRntrc",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "OwnerType",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "Renavam",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "WheelType",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "Inactive",
                table: "TRUCK_DRIVERS");

            migrationBuilder.DropColumn(
                name: "LicenseCategory",
                table: "TRUCK_DRIVERS");

            migrationBuilder.DropColumn(
                name: "LicenseExpiresOn",
                table: "TRUCK_DRIVERS");

            migrationBuilder.DropColumn(
                name: "LicenseNumber",
                table: "TRUCK_DRIVERS");
        }
    }
}
