using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Mercado, parte 3 (hedges): cinco telas filhas de "market" (Operações de Bolsa, Hedge de Moeda, EFP, Ajustes e
    /// Margem, Corretoras e Bancos). As Keys PRECISAM ser iguais ao name das rotas do manifest.json do frontend.
    /// ROLE_MENUS (GUIDs fixos) para ADMIN, MARKET_TRADER e MARKET_BOARD. Idempotente: o item de menu só entra se
    /// não existe; a linha de papel só entra se o papel existe e a linha ainda não existe. O Down remove
    /// exatamente o que o Up inseriu.
    /// </summary>
    public partial class AddMarketHedgesMenu : Migration
    {
        private static readonly (string Key, string Title, int Order)[] Menus =
        [
            ("marketFuturesTrades", "Operações de Bolsa", 40),
            ("marketFxHedges", "Hedge de Moeda", 42),
            ("marketEfps", "EFP", 44),
            ("marketBrokerStatements", "Ajustes e Margem", 46),
            ("marketCounterparties", "Corretoras e Bancos", 48),
        ];

        private static readonly (string Id, string Role, string MenuKey)[] Rows =
        [
            ("6422B166-29B8-4A4E-B780-97D64A2741C3", "ADMIN", "marketFuturesTrades"),
            ("65FD1747-3239-40F7-AD03-6A05F5FE6F86", "ADMIN", "marketFxHedges"),
            ("23BCAE8D-A36F-4518-9AB2-9906467C703D", "ADMIN", "marketEfps"),
            ("3C01DFC4-4BD3-48B2-9397-5EDD148F1452", "ADMIN", "marketBrokerStatements"),
            ("78627934-F0A5-44B1-8522-2AE350339DF7", "ADMIN", "marketCounterparties"),
            ("5DB9C31C-5401-44B7-9435-049F4E8A4C94", "MARKET_TRADER", "marketFuturesTrades"),
            ("AE9A3438-91C4-49AA-9603-B47CC7F4EEBB", "MARKET_TRADER", "marketFxHedges"),
            ("639FC3F4-B799-4296-869A-FFD5E8F8DF80", "MARKET_TRADER", "marketEfps"),
            ("107C39C4-988E-4186-9085-E87FFF148FAD", "MARKET_TRADER", "marketBrokerStatements"),
            ("0D2F27CC-A35D-47FF-9FDE-AFCFC76978C4", "MARKET_TRADER", "marketCounterparties"),
            ("5CB53C74-B202-4BF4-8D77-65C772DB277F", "MARKET_BOARD", "marketFuturesTrades"),
            ("3B642254-D23D-45D8-A1BD-F88A0AB2745D", "MARKET_BOARD", "marketFxHedges"),
            ("007B87B7-AAB9-43AA-80BC-7D78041CDF12", "MARKET_BOARD", "marketEfps"),
            ("72F296D0-AC96-47C5-8DEE-8869C862B9CF", "MARKET_BOARD", "marketBrokerStatements"),
            ("1722B882-C3DA-4D07-9F42-4C6D2D3DDD60", "MARKET_BOARD", "marketCounterparties"),
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
