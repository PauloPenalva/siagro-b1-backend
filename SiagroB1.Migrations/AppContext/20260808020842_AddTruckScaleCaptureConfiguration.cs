using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTruckScaleCaptureConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FirstWeighCaptured",
                table: "WEIGHING_TICKETS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FirstWeighScaleCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(11)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecondWeighCaptured",
                table: "WEIGHING_TICKETS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecondWeighScaleCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TareWeight",
                table: "TRUCKS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecimalPlaces",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FramePattern",
                table: "TRUCK_SCALES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FramePrefixLength",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrameTerminator",
                table: "TRUCK_SCALES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "TRUCK_SCALES",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LogRawFrames",
                table: "TRUCK_SCALES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Protocol",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TareToleranceKg",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ValidateTare",
                table: "TRUCK_SCALES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WeightLength",
                table: "TRUCK_SCALES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "USER_TRUCK_SCALES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "VARCHAR(50)", maxLength: 50, nullable: false),
                    TruckScaleCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_TRUCK_SCALES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_USER_TRUCK_SCALES_TRUCK_SCALES_TruckScaleCode",
                        column: x => x.TruckScaleCode,
                        principalTable: "TRUCK_SCALES",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_TRUCK_SCALES_TruckScaleCode",
                table: "USER_TRUCK_SCALES",
                column: "TruckScaleCode");

            migrationBuilder.CreateIndex(
                name: "IX_USER_TRUCK_SCALES_Username_Purpose",
                table: "USER_TRUCK_SCALES",
                columns: new[] { "Username", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "FirstWeighCaptured",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "FirstWeighScaleCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "SecondWeighCaptured",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "SecondWeighScaleCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "TareWeight",
                table: "TRUCKS");

            migrationBuilder.DropColumn(
                name: "DecimalPlaces",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "FramePattern",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "FramePrefixLength",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "FrameTerminator",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "LogRawFrames",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "Protocol",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "TareToleranceKg",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "ValidateTare",
                table: "TRUCK_SCALES");

            migrationBuilder.DropColumn(
                name: "WeightLength",
                table: "TRUCK_SCALES");
        }
    }
}
