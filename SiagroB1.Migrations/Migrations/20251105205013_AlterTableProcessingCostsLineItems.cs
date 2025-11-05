using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostsLineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
