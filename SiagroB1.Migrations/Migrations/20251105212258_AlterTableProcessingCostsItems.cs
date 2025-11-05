using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostsItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ItemId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ItemId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ItemId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 2);

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 2);

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 2);

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 2);

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "BranchKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_BRANCHS_BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");
        }
    }
}
