using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddFiscalConferenceMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: a conferência fiscal só existe em STANDALONE. Order 5: logo após os 4 itens
            // que o AddFiscalMenu moveu para o grupo "fiscal" (ordens 1 a 4).
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "fiscalConference", "Conferência Fiscal", "sap-icon://inspection", true, false, 5, "fiscal", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "3C7A9E15-8B24-4D60-A1F3-6E5B2D90C84A", "ADMIN", "fiscalConference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "3C7A9E15-8B24-4D60-A1F3-6E5B2D90C84A");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "fiscalConference");
        }
    }
}
