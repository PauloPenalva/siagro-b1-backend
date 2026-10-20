using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    /// <summary>
    /// Menu "Manifestação do Destinatário" sob "Fiscal" (Order 5; 1–4 já ocupados). Só STANDALONE: a Distribuição
    /// de DF-e usa o certificado A1 da filial, que só existe nesse modo.
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddReceivedDfesMenu : Migration
    {
        private const string RoleMenuId = "7C2D5A9E-1B84-4F36-A0E7-93D6B4C81F25";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "receivedDfes", "Manifestação do Destinatário", "sap-icon://inbox", true, false, 5, "fiscal", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { RoleMenuId, "ADMIN", "receivedDfes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: RoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "receivedDfes");
        }
    }
}
