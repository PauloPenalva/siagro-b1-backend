using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Módulo "TMS" no menu (só STANDALONE): reúne Conhecimentos de Transporte (CT-e), Manifestos (MDF-e)
    /// e os cadastros de transporte (Veículos, Motoristas, Conjuntos de Veículos). Os itens existentes
    /// só trocam de pai/ordem; ROLE_MENUS segue a chave do item, então as permissões não mudam. O módulo
    /// em si precisa de ROLE_MENUS (o MenuService descarta filho sem o pai), por isso todo perfil que já
    /// tem um dos itens movidos ganha também o "tms".
    /// </summary>
    public partial class AddTmsMenuModule : Migration
    {
        private const string TmsRoleMenuId = "6D2F8B14-3A97-4E05-B1C8-7A4E0D95F362";
        private const string ManifestsRoleMenuId = "9C41E7A3-5B28-4F6D-8E10-2D7B63A8C95F";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "tms", "TMS", "sap-icon://folder-blank", true, false, 10, null, true });

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "transportManifests", "Manifestos (MDF-e)", "sap-icon://shipping-status", true, false, 2, "tms", true });

            Move(migrationBuilder, "transportDocuments", "tms", 1);
            Move(migrationBuilder, "veiculos", "tms", 3);
            Move(migrationBuilder, "motoristas", "tms", 4);
            Move(migrationBuilder, "truckCombinations", "tms", 5);

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { TmsRoleMenuId, "ADMIN", "tms" },
                    { ManifestsRoleMenuId, "ADMIN", "transportManifests" }
                });

            // Demais perfis que já enxergam CT-e ou cadastro de transporte continuam vendo o módulo.
            migrationBuilder.Sql(@"
INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey)
SELECT NEWID(), r.RoleCode, 'tms'
FROM (SELECT DISTINCT RoleCode FROM ROLE_MENUS
      WHERE MenuItemKey IN ('transportDocuments', 'veiculos', 'motoristas', 'truckCombinations')) r
WHERE NOT EXISTS (SELECT 1 FROM ROLE_MENUS x WHERE x.RoleCode = r.RoleCode AND x.MenuItemKey = 'tms');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Move(migrationBuilder, "transportDocuments", "sales", 16);
            Move(migrationBuilder, "veiculos", "registers", 4);
            Move(migrationBuilder, "motoristas", "registers", 3);
            Move(migrationBuilder, "truckCombinations", "registers", 4);

            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ('tms', 'transportManifests');");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] IN ('transportManifests', 'tms');");
        }

        private static void Move(MigrationBuilder migrationBuilder, string key, string parentKey, int order) =>
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: key,
                columns: ["ParentKey", "Order"],
                values: new object[] { parentKey, order });
    }
}
