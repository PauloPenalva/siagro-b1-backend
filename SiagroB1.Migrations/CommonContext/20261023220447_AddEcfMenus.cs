using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddEcfMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend. Grupo "accounting",
            // ordens 19 a 21 (depois de ecdSettings = 18). StandaloneOnly: a ECF só existe em STANDALONE.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "ecf", "ECF (Escrituração Contábil Fiscal)", "sap-icon://document-text", true, false, 19, "accounting", true },
                    { "ecfSettings", "Parâmetros da ECF", "sap-icon://action-settings", true, false, 20, "accounting", true },
                    { "ecfAdjustments", "Ajustes do LALUR/LACS", "sap-icon://edit", true, false, 21, "accounting", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C01", "ADMIN", "ecf" },
                    { "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C02", "ADMIN", "ecfSettings" },
                    { "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C03", "ADMIN", "ecfAdjustments" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C01");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C02");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "EC0F1A2B-7C25-4E83-8B69-4E8F9A0B1C03");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "ecf");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "ecfSettings");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "ecfAdjustments");
        }
    }
}
