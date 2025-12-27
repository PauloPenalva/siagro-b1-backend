#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnChaveNFe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveNFe",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(44)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceQty",
                table: "STORAGE_TRANSACTIONS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChaveNFe",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "InvoiceQty",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
