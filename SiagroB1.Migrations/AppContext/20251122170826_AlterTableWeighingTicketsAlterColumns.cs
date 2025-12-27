#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableWeighingTicketsAlterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QUALITY_INSPECTIONS_WEIGHTING_TICKETS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WEIGHTING_TICKETS",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropColumn(
                name: "ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.RenameTable(
                name: "WEIGHTING_TICKETS",
                newName: "WEIGHING_TICKETS");

            migrationBuilder.RenameColumn(
                name: "GrossWeight",
                table: "WEIGHING_TICKETS",
                newName: "Type");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHTING_TICKETS_TruckDriverCode",
                table: "WEIGHING_TICKETS",
                newName: "IX_WEIGHING_TICKETS_TruckDriverCode");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHTING_TICKETS_TruckCode",
                table: "WEIGHING_TICKETS",
                newName: "IX_WEIGHING_TICKETS_TruckCode");

            migrationBuilder.AlterColumn<string>(
                name: "Time",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(15)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SecondWeighDateTime",
                table: "WEIGHING_TICKETS",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstWeighDateTime",
                table: "WEIGHING_TICKETS",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "WEIGHING_TICKETS",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "CardCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(15)",
                maxLength: 15,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "WEIGHING_TICKETS",
                type: "int",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_WEIGHING_TICKETS",
                table: "WEIGHING_TICKETS",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_QUALITY_INSPECTIONS_WEIGHING_TICKETS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS",
                column: "WeighingTicketKey",
                principalTable: "WEIGHING_TICKETS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS",
                column: "TruckCode",
                principalTable: "TRUCKS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS",
                column: "TruckDriverCode",
                principalTable: "TRUCK_DRIVERS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QUALITY_INSPECTIONS_WEIGHING_TICKETS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WEIGHING_TICKETS",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WEIGHING_TICKETS");

            migrationBuilder.RenameTable(
                name: "WEIGHING_TICKETS",
                newName: "WEIGHTING_TICKETS");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "WEIGHTING_TICKETS",
                newName: "GrossWeight");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHING_TICKETS_TruckDriverCode",
                table: "WEIGHTING_TICKETS",
                newName: "IX_WEIGHTING_TICKETS_TruckDriverCode");

            migrationBuilder.RenameIndex(
                name: "IX_WEIGHING_TICKETS_TruckCode",
                table: "WEIGHTING_TICKETS",
                newName: "IX_WEIGHTING_TICKETS_TruckCode");

            migrationBuilder.AlterColumn<int>(
                name: "Time",
                table: "WEIGHTING_TICKETS",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SecondWeighDateTime",
                table: "WEIGHTING_TICKETS",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "WEIGHTING_TICKETS",
                type: "VARCHAR(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstWeighDateTime",
                table: "WEIGHTING_TICKETS",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "WEIGHTING_TICKETS",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CardCode",
                table: "WEIGHTING_TICKETS",
                type: "VARCHAR(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldMaxLength: 15);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "WEIGHTING_TICKETS",
                type: "VARCHAR(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_WEIGHTING_TICKETS",
                table: "WEIGHTING_TICKETS",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostCode");

            migrationBuilder.AddForeignKey(
                name: "FK_QUALITY_INSPECTIONS_WEIGHTING_TICKETS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS",
                column: "WeighingTicketKey",
                principalTable: "WEIGHTING_TICKETS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_TRUCKS_TruckCode",
                table: "WEIGHTING_TICKETS",
                column: "TruckCode",
                principalTable: "TRUCKS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_TRUCK_DRIVERS_TruckDriverCode",
                table: "WEIGHTING_TICKETS",
                column: "TruckDriverCode",
                principalTable: "TRUCK_DRIVERS",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
