#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContracts202511191955 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
