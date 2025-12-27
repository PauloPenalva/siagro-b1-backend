#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableProcessingService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_SERVICES_BRANCHS_BranchKey",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_SERVICES",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_SERVICES_BranchKey",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropColumn(
                name: "BranchKey",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropColumn(
                name: "ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "PROCESSING_SERVICES",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_SERVICES",
                table: "PROCESSING_SERVICES",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceCode",
                principalTable: "PROCESSING_SERVICES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PROCESSING_SERVICES",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "PROCESSING_SERVICES");

            migrationBuilder.DropColumn(
                name: "ProcessingServiceCode",
                table: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "PROCESSING_SERVICES",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BranchKey",
                table: "PROCESSING_SERVICES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_PROCESSING_SERVICES",
                table: "PROCESSING_SERVICES",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_SERVICES_BranchKey",
                table: "PROCESSING_SERVICES",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceKey",
                principalTable: "PROCESSING_SERVICES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PROCESSING_SERVICES_BRANCHS_BranchKey",
                table: "PROCESSING_SERVICES",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
