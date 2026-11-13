using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTruckScaleSerialConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaudRate",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionType",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DataBits",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Parity",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SerialPortName",
                table: "TRUCK_SCALES",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StopBits",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaudRate",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "ConnectionType",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "DataBits",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "Parity",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "SerialPortName",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "StopBits",
                table: "TRUCK_SCALES");
        }
    }
}
