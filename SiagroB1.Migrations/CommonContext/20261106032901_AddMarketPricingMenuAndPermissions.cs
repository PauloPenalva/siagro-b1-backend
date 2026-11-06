using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Mercado, parte 2 (preço por cidade + boleta): seis telas filhas de "market" e as permissões
    /// MARKET_MANAGE / MARKET_TICKET_APPROVE / MARKET_BOARD_APPROVE com os papéis MARKET_TRADER e MARKET_BOARD.
    /// As Keys PRECISAM ser iguais ao name das rotas do manifest.json do frontend. Com ParentKey = 'market' o
    /// MarketMenuFilter esconde tudo quando Modules:Market:Enabled está desligado. ADMIN recebe os itens de menu
    /// (GUIDs fixos) e as três permissões; os papéis novos NÃO recebem menus (montados no cadastro de perfis).
    /// Tudo idempotente: o código pode já ter sido cadastrado à mão.
    /// </summary>
    public partial class AddMarketPricingMenuAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketPriceGrid')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketPriceGrid', 'Ordem de Compra', 'sap-icon://folder-blank', 1, 0, 12, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketTradeTickets')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketTradeTickets', 'Boletas', 'sap-icon://folder-blank', 1, 0, 14, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketPriceTables')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketPriceTables', 'Tabela de Preços', 'sap-icon://folder-blank', 1, 0, 16, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketFreightRates')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketFreightRates', 'Tabela de Frete', 'sap-icon://folder-blank', 1, 0, 18, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketDestinations')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketDestinations', 'Destinos', 'sap-icon://folder-blank', 1, 0, 19, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketTradingSettings')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketTradingSettings', 'Parâmetros do Mercado', 'sap-icon://folder-blank', 1, 0, 25, 'market');
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '55349EE5-3D4F-48DF-B5FE-D4EDA20B04B1')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('55349EE5-3D4F-48DF-B5FE-D4EDA20B04B1', 'ADMIN', 'marketPriceGrid');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = 'ABC44E3F-1C8E-4942-9DA5-56DFCE30F46B')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('ABC44E3F-1C8E-4942-9DA5-56DFCE30F46B', 'ADMIN', 'marketTradeTickets');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = 'EB5AACF1-2B04-46A6-AC20-44CF18838118')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('EB5AACF1-2B04-46A6-AC20-44CF18838118', 'ADMIN', 'marketPriceTables');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '87A4A7BD-10BF-4AF5-B161-05F2466C6ADB')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('87A4A7BD-10BF-4AF5-B161-05F2466C6ADB', 'ADMIN', 'marketFreightRates');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = 'F3948410-054B-436A-8F6F-854D8E20E037')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('F3948410-054B-436A-8F6F-854D8E20E037', 'ADMIN', 'marketDestinations');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '750419B5-CF8C-489D-B5DB-E9585BCE07F0')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('750419B5-CF8C-489D-B5DB-E9585BCE07F0', 'ADMIN', 'marketTradingSettings');
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'MARKET_MANAGE')
                    INSERT INTO PERMISSIONS (Code, Description) VALUES ('MARKET_MANAGE', 'Mercado: manter cadastros e cotações');
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'MARKET_TICKET_APPROVE')
                    INSERT INTO PERMISSIONS (Code, Description) VALUES ('MARKET_TICKET_APPROVE', 'Mercado: aprovar boletas');
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'MARKET_BOARD_APPROVE')
                    INSERT INTO PERMISSIONS (Code, Description) VALUES ('MARKET_BOARD_APPROVE', 'Mercado: aprovar boletas na alçada da diretoria');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'MARKET_TRADER')
                    INSERT INTO ROLES (Code, Description) VALUES ('MARKET_TRADER', 'Trader do Mercado');
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'MARKET_BOARD')
                    INSERT INTO ROLES (Code, Description) VALUES ('MARKET_BOARD', 'Diretoria (Mercado)');
                """);

            // (papel, permissão): trader = manage + ticket approve; diretoria = ticket approve + board approve; ADMIN = as três.
            foreach (var (role, permission) in new[]
            {
                ("MARKET_TRADER", "MARKET_MANAGE"), ("MARKET_TRADER", "MARKET_TICKET_APPROVE"),
                ("MARKET_BOARD", "MARKET_TICKET_APPROVE"), ("MARKET_BOARD", "MARKET_BOARD_APPROVE"),
                ("ADMIN", "MARKET_MANAGE"), ("ADMIN", "MARKET_TICKET_APPROVE"), ("ADMIN", "MARKET_BOARD_APPROVE"),
            })
            {
                migrationBuilder.Sql($"""
                    IF EXISTS (SELECT 1 FROM ROLES WHERE Code = '{role}')
                       AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = '{permission}')
                       AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = '{role}' AND PermissionCode = '{permission}')
                        INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), '{role}', '{permission}');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode IN ('MARKET_MANAGE', 'MARKET_TICKET_APPROVE', 'MARKET_BOARD_APPROVE') OR RoleCode IN ('MARKET_TRADER', 'MARKET_BOARD');");
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE RoleCode IN ('MARKET_TRADER', 'MARKET_BOARD');");
            migrationBuilder.Sql("DELETE FROM ROLES WHERE Code IN ('MARKET_TRADER', 'MARKET_BOARD');");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code IN ('MARKET_MANAGE', 'MARKET_TICKET_APPROVE', 'MARKET_BOARD_APPROVE');");

            foreach (var id in new[]
            {
                "55349EE5-3D4F-48DF-B5FE-D4EDA20B04B1", "ABC44E3F-1C8E-4942-9DA5-56DFCE30F46B",
                "EB5AACF1-2B04-46A6-AC20-44CF18838118", "87A4A7BD-10BF-4AF5-B161-05F2466C6ADB",
                "F3948410-054B-436A-8F6F-854D8E20E037", "750419B5-CF8C-489D-B5DB-E9585BCE07F0"
            })
            {
                migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: id);
            }

            migrationBuilder.Sql("""
                DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ('marketPriceGrid', 'marketTradeTickets', 'marketPriceTables', 'marketFreightRates', 'marketDestinations', 'marketTradingSettings');
                DELETE FROM MENU_ITEMS WHERE [Key] IN ('marketPriceGrid', 'marketTradeTickets', 'marketPriceTables', 'marketFreightRates', 'marketDestinations', 'marketTradingSettings');
                """);
        }
    }
}
