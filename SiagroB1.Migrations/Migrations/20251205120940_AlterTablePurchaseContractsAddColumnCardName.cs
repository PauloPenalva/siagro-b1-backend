using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumnCardName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardName",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
