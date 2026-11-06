using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Os papéis MARKET_TRADER e MARKET_BOARD não tinham ROLE_MENUS: quem tinha só um deles não via o menu Mercado.
    /// Dá aos dois todos os itens market* (inclusive o pai "market"), com GUIDs fixos. Idempotente: só insere se o
    /// papel e o item de menu existem e a linha ainda não existe; o Down remove exatamente estas linhas.
    /// </summary>
    public partial class AddMarketRoleMenus : Migration
    {
        private static readonly (string Id, string Role, string MenuKey)[] Rows =
        [
            ("BF7DCE21-ED06-463B-9788-C6C6750E7383", "MARKET_TRADER", "market"),
            ("D25F54C3-C3DD-4B9C-8892-1F522439DB6A", "MARKET_TRADER", "marketQuotesTerminal"),
            ("176FB817-DBC9-4CD2-B880-1507333CD9D9", "MARKET_TRADER", "marketInstruments"),
            ("75DBD497-E244-40FF-93FC-8718996E5F8B", "MARKET_TRADER", "marketCmaSettings"),
            ("CC95359D-7F43-4D1D-A04F-492A4E66AEA9", "MARKET_TRADER", "marketPriceGrid"),
            ("BD7218C8-7D45-4569-9886-EB760F19EEFF", "MARKET_TRADER", "marketTradeTickets"),
            ("1B7325FE-230C-42EF-A3CE-174778577A4C", "MARKET_TRADER", "marketPriceTables"),
            ("6A8844C3-BA8A-4D0E-81F7-F5771B48F039", "MARKET_TRADER", "marketFreightRates"),
            ("484739E8-B060-4036-B84C-9CDBDBCF28C5", "MARKET_TRADER", "marketDestinations"),
            ("20DF7B35-DDB4-44CB-A2DC-4053BAA8B6C2", "MARKET_TRADER", "marketTradingSettings"),
            ("2F43E17B-FE99-46A1-ACDD-3F2BB9B183F9", "MARKET_BOARD", "market"),
            ("C9529377-E87A-4C7D-BE40-D5B857B83C19", "MARKET_BOARD", "marketQuotesTerminal"),
            ("3FCA9227-F8B5-41B4-A35D-2205B4D5EAE4", "MARKET_BOARD", "marketInstruments"),
            ("AD173E3E-917E-493B-A9E5-2341A541F8D4", "MARKET_BOARD", "marketCmaSettings"),
            ("493953CD-4AE0-4B19-BBD6-190F213B7608", "MARKET_BOARD", "marketPriceGrid"),
            ("078D3E5D-C190-416D-96E5-4DA23D19C3D8", "MARKET_BOARD", "marketTradeTickets"),
            ("8131E217-E44A-4D90-ABC9-32D09A1BE458", "MARKET_BOARD", "marketPriceTables"),
            ("3D33B59C-96DC-44BB-947B-C47659EDA20C", "MARKET_BOARD", "marketFreightRates"),
            ("F627BE58-0912-41E3-B4CF-7CDDC71FCEE5", "MARKET_BOARD", "marketDestinations"),
            ("CCFA18D0-7EAE-47EE-AE1A-4E9B6697874A", "MARKET_BOARD", "marketTradingSettings"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
