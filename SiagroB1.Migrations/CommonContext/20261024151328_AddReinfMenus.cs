using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddReinfMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend. Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: a EFD-Reinf só existe em STANDALONE. Ordens 12 e 13: após a EFD Contribuições.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "reinf", "EFD-Reinf", "sap-icon://document-text", true, false, 12, "fiscal", true },
                    { "reinfSettings", "Parâmetros da EFD-Reinf", "sap-icon://document-text", true, false, 13, "fiscal", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7B2D4E91-3C58-4A06-9F1B-D84E6A0C5173", "ADMIN", "reinf" },
                    { "A94F1C6E-82B7-4D35-B0E9-3F7A5D2C8614", "ADMIN", "reinfSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "7B2D4E91-3C58-4A06-9F1B-D84E6A0C5173");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "A94F1C6E-82B7-4D35-B0E9-3F7A5D2C8614");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "reinf");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "reinfSettings");
        }
    }
}
