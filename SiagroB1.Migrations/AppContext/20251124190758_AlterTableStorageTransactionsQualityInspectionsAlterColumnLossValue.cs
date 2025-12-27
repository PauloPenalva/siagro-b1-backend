#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsQualityInspectionsAlterColumnLossValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LossValue",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                type: "DECIMAL(18,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LossValue",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS");
        }
    }
}
