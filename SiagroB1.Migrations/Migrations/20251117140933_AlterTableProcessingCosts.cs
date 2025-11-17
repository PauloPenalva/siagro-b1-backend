using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCosts : Migration
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

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COSTS_BRANCHS_BranchKey",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostKey",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostKey",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COSTS",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COSTS_BranchKey",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessigCostKey",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.AddColumn<string>(
                name: "ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "PROCESSING_COSTS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COSTS",
                table: "PROCESSING_COSTS",
                column: "Code");

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

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COSTS",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_SERVICE_DETAILS",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_QUALITY_PARAMETERS",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_PARAMETERS",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_COST_DRYING_DETAILS",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessigCostCode",
                table: "WEIGHTING_TICKETS");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessigCostKey",
                table: "WEIGHTING_TICKETS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_COSTS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_COSTS",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_COSTS",
                table: "PROCESSING_COSTS",
                column: "Key");

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
                name: "IX_WEIGHTING_TICKETS_ProcessigCostKey",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COSTS_BranchKey",
                table: "PROCESSING_COSTS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostKey");

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

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COSTS_BRANCHS_BranchKey",
                table: "PROCESSING_COSTS",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostKey",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostKey",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Key");
        }
    }
}
