#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableDocTypesAddColumnBranchCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "DOC_TYPES",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.CreateIndex(
                name: "IX_DOC_TYPES_BranchCode",
                table: "DOC_TYPES",
                column: "BranchCode");

            migrationBuilder.AddForeignKey(
                name: "FK_DOC_TYPES_BRANCHS_BranchCode",
                table: "DOC_TYPES",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DOC_TYPES_BRANCHS_BranchCode",
                table: "DOC_TYPES");

            migrationBuilder.DropIndex(
                name: "IX_DOC_TYPES_BranchCode",
                table: "DOC_TYPES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "DOC_TYPES");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(14)",
                nullable: false,
                defaultValue: "")
                .Annotation("Relational:ColumnOrder", 1);
        }
    }
}
