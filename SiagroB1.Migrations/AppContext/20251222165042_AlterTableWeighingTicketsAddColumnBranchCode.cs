#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableWeighingTicketsAddColumnBranchCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_TYPES_DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "WEIGHING_TICKETS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_DocNumberKey",
                table: "WEIGHING_TICKETS",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_NUMBERS_DocNumberKey",
                table: "WEIGHING_TICKETS",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_NUMBERS_DocNumberKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_DocNumberKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_DocTypeCode",
                table: "WEIGHING_TICKETS",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_DOC_TYPES_DocTypeCode",
                table: "WEIGHING_TICKETS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
