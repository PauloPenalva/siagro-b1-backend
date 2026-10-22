using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Item "Conjuntos" em Cadastros (só STANDALONE), ao lado de Veículos. A Key é o name da rota no
    /// manifest do frontend. A configuração de transporte fica na tela da filial: não ganha menu.
    /// </summary>
    public partial class AddTransportMasterDataMenus : Migration
    {
        private const string RoleMenuId = "6F1D2C3B-5A49-4E8D-9C17-2B4E8A0F3D61";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "truckCombinations", "Conjuntos de Veículos", "sap-icon://shipping-status", true, false, 4, "registers", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { RoleMenuId, "ADMIN", "truckCombinations" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: RoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "truckCombinations");
        }
    }
}
