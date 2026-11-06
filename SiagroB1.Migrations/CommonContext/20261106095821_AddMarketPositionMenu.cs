using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Mercado, parte 4 (posição): três telas filhas de "market" (Posição, Limites de Exposição, Commodities e
    /// Vencimentos). As Keys PRECISAM ser iguais ao name das rotas do manifest.json do frontend.
    /// ROLE_MENUS (GUIDs fixos) para ADMIN, MARKET_TRADER e MARKET_BOARD. Idempotente: o item de menu só entra se
    /// não existe; a linha de papel só entra se o papel existe e a linha ainda não existe. O Down remove
    /// exatamente o que o Up inseriu.
    /// </summary>
    public partial class AddMarketPositionMenu : Migration
    {
        private static readonly (string Key, string Title, int Order)[] Menus =
        [
            ("marketPosition", "Posição", 5),
            ("marketExposureLimits", "Limites de Exposição", 50),
            ("marketCommodities", "Commodities e Vencimentos", 52),
        ];

        private static readonly (string Id, string Role, string MenuKey)[] Rows =
        [
            ("5398D9BC-B6D5-4145-9331-7DE8D52E4D54", "ADMIN", "marketPosition"),
            ("B5659B7A-B379-4B00-B3AD-70D1B5DD9D1B", "ADMIN", "marketExposureLimits"),
            ("FFF5C2F2-A752-491B-845E-D2E0050FD4FB", "ADMIN", "marketCommodities"),
            ("4F4C809B-1A05-4D2B-9103-06B6E7802D01", "MARKET_TRADER", "marketPosition"),
            ("F6CACA42-ADF0-48E9-A31E-8DF38A09AD4C", "MARKET_TRADER", "marketExposureLimits"),
            ("24F27B73-77E3-4A83-A21C-6013490E3F4C", "MARKET_TRADER", "marketCommodities"),
            ("6F156AD7-FB8C-4A49-9D19-14D0ED15E1B5", "MARKET_BOARD", "marketPosition"),
            ("F6649389-0E77-4FE6-BD43-C125F06F8717", "MARKET_BOARD", "marketExposureLimits"),
            ("BA485BB9-E641-45A7-9CA9-D43ADA5D7A3A", "MARKET_BOARD", "marketCommodities"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (key, title, order) in Menus)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = '{key}')
                        INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                        VALUES ('{key}', '{title}', 'sap-icon://folder-blank', 1, 0, {order}, 'market');
                    """);
            }

            foreach (var (id, role, menuKey) in Rows)
            {
                migrationBuilder.Sql($"""
                    IF EXISTS (SELECT 1 FROM ROLES WHERE Code = '{role}')
                       AND EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = '{menuKey}')
                       AND NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '{id}')
                       AND NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = '{role}' AND MenuItemKey = '{menuKey}')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('{id}', '{role}', '{menuKey}');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, _, _) in Rows)
                migrationBuilder.Sql($"DELETE FROM ROLE_MENUS WHERE Id = '{id}';");

            foreach (var (key, _, _) in Menus)
                migrationBuilder.Sql($"DELETE FROM MENU_ITEMS WHERE [Key] = '{key}';");
        }
    }
}
