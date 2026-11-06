using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Módulo "Mercado" (cotações) no menu superior, por ÚLTIMO: os bancos reais usam Order 10, 20, ... 120 no
    /// nível superior (menus reorganizados), então o Order é calculado (maior + 10), não fixo. Filhos 10/20/30
    /// no espaçamento da casa. As Keys PRECISAM ser iguais ao name das rotas do manifest.json do frontend
    /// (App.controller.ts navega com navTo(item.getKey())). A migration vale para todos os bancos; o MenuService
    /// esconde a árvore "market" quando Modules:Market:Enabled está desligado.
    /// </summary>
    public partial class AddMarketMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'market')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    SELECT 'market', 'Mercado', 'sap-icon://folder-blank', 1, 0, ISNULL(MAX([Order]), 0) + 10, NULL
                    FROM MENU_ITEMS WHERE ParentKey IS NULL OR ParentKey = '';
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketQuotesTerminal')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketQuotesTerminal', 'Terminal de Cotações', 'sap-icon://folder-blank', 1, 0, 10, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketInstruments')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketInstruments', 'Instrumentos', 'sap-icon://folder-blank', 1, 0, 20, 'market');
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'marketCmaSettings')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('marketCmaSettings', 'Integração CMA', 'sap-icon://folder-blank', 1, 0, 30, 'market');
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '5E3A9C71-0B4D-4F82-A6E1-7C29D8B3F054')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('5E3A9C71-0B4D-4F82-A6E1-7C29D8B3F054', 'ADMIN', 'market');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = 'A81F6D20-3C57-4E9B-8D14-62B0E7A5C913')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('A81F6D20-3C57-4E9B-8D14-62B0E7A5C913', 'ADMIN', 'marketQuotesTerminal');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = 'C47B0E95-81D2-4A36-9F58-1E6D3A2B7C80')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('C47B0E95-81D2-4A36-9F58-1E6D3A2B7C80', 'ADMIN', 'marketInstruments');
                    IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE Id = '13D9F5A8-6E04-4B7C-A2D1-9B85C0E4F726')
                        INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES ('13D9F5A8-6E04-4B7C-A2D1-9B85C0E4F726', 'ADMIN', 'marketCmaSettings');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var id in new[]
            {
                "5E3A9C71-0B4D-4F82-A6E1-7C29D8B3F054", "A81F6D20-3C57-4E9B-8D14-62B0E7A5C913",
                "C47B0E95-81D2-4A36-9F58-1E6D3A2B7C80", "13D9F5A8-6E04-4B7C-A2D1-9B85C0E4F726"
            })
            {
                migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: id);
            }

            // Filhos antes do pai (FK ParentKey).
            foreach (var key in new[] { "marketCmaSettings", "marketInstruments", "marketQuotesTerminal", "market" })
            {
                migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: key);
            }
        }
    }
}
