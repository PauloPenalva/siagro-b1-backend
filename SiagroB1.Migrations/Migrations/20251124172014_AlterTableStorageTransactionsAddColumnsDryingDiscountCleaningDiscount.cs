using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnsDryingDiscountCleaningDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CleaningDiscount",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DryingDiscount",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OthersDicount",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleaningDiscount",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DryingDiscount",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "OthersDicount",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
