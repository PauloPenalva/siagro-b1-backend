using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Item "Conhecimentos de Transporte" no módulo Vendas (só STANDALONE). A Key é o name da rota no
    /// manifest do frontend.
    /// </summary>
    public partial class AddCteMenus : Migration
    {
        private const string RoleMenuId = "3B7E9A52-8C14-4D6F-A1E0-5F2C9D7B4E18";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "transportDocuments", "Conhecimentos de Transporte", "sap-icon://shipping-status", true, false, 16, "sales", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { RoleMenuId, "ADMIN", "transportDocuments" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: RoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "transportDocuments");
        }
    }
}
