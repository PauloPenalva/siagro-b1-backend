using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddEcdMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend. Grupo "accounting",
            // ordens 17 e 18 (depois de balanceSheetReport = 16). StandaloneOnly: a ECD só existe em STANDALONE.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "ecd", "ECD (SPED Contábil)", "sap-icon://document-text", true, false, 17, "accounting", true },
                    { "ecdSettings", "Parâmetros da ECD", "sap-icon://action-settings", true, false, 18, "accounting", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "E1C0D2A3-6B14-4F72-9A58-3D7E8F9A0B01", "ADMIN", "ecd" },
                    { "E1C0D2A3-6B14-4F72-9A58-3D7E8F9A0B02", "ADMIN", "ecdSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "E1C0D2A3-6B14-4F72-9A58-3D7E8F9A0B01");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "E1C0D2A3-6B14-4F72-9A58-3D7E8F9A0B02");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "ecd");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "ecdSettings");
        }
    }
}
