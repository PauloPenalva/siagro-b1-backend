using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BaseEntityAddColumnRowId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "WEIGHING_TICKETS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "STORAGE_ADDRESSES",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "SHIPPING_ORDERS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "SHIPMENT_RELEASES",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "SALES_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "SALES_CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowId",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
