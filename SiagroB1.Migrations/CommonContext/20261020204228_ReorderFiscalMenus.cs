using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// A Manifestação do Destinatário (receivedDfes) e a Conferência Fiscal nasceram em branches
    /// diferentes, ambas com Order 5 no grupo Fiscal. Reordena as telas fiscais novas:
    /// Manifestação 5, Conferência 6, Saldo Inicial 7, Kardex 8, Inventário 9.
    /// </summary>
    public partial class ReorderFiscalMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SetOrder(migrationBuilder, "fiscalConference", 6);
            SetOrder(migrationBuilder, "fiscalInventoryOpeningBalances", 7);
            SetOrder(migrationBuilder, "fiscalKardex", 8);
            SetOrder(migrationBuilder, "fiscalInventory", 9);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            SetOrder(migrationBuilder, "fiscalConference", 5);
            SetOrder(migrationBuilder, "fiscalInventoryOpeningBalances", 6);
            SetOrder(migrationBuilder, "fiscalKardex", 7);
            SetOrder(migrationBuilder, "fiscalInventory", 8);
        }

        private static void SetOrder(MigrationBuilder migrationBuilder, string key, int order) =>
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: key,
                column: "Order",
                value: order);
    }
}
