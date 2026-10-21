using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Módulo raiz "Contabilidade" (pedido do usuário, 21/10), logo depois de Financeiro (7), no padrão
    /// de AddFinancialMenus (raiz = ParentKey null). Plano de Contas e Centro de Custo saem de Cadastros;
    /// Item Contábil e Rateios Pré-configurados são novos e só STANDALONE. A raiz não é StandaloneOnly
    /// porque em SAPB1 Plano de Contas e Centro de Custo continuam visíveis (somente leitura, do SAP).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend. Sem ROLE_MENUS o item não
    /// aparece para ninguém — inclusive o próprio grupo.
    /// </summary>
    public partial class AddAccountingModuleMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deslocamento relativo dos raízes a partir de 8 (bancos podem ter raízes extras).
            migrationBuilder.Sql(
                "UPDATE MENU_ITEMS SET [Order] = [Order] + 1 WHERE ParentKey IS NULL AND [Order] >= 8 AND [Key] <> 'accounting'");

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "accounting", "Contabilidade", "sap-icon://accounting-document-verification", true, false, 8, null, false });

            migrationBuilder.Sql("UPDATE MENU_ITEMS SET ParentKey = 'accounting', [Order] = 1, Title = 'Plano de Contas' WHERE [Key] = 'ledgerAccounts'");
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET ParentKey = 'accounting', [Order] = 2, Title = 'Centro de Custo' WHERE [Key] = 'costCenters'");

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "accountingItems", "Item Contábil", "sap-icon://folder-blank", true, false, 3, "accounting", true },
                    { "distributionRules", "Rateios Pré-configurados", "sap-icon://split", true, false, 4, "accounting", true },
                });

            // A raiz vai para todo perfil que já enxergava Plano de Contas ou Centro de Custo.
            migrationBuilder.Sql(
                "INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) " +
                "SELECT NEWID(), r.RoleCode, 'accounting' FROM " +
                "(SELECT DISTINCT RoleCode FROM ROLE_MENUS WHERE MenuItemKey IN ('ledgerAccounts','costCenters')) r " +
                "WHERE NOT EXISTS (SELECT 1 FROM ROLE_MENUS x WHERE x.RoleCode = r.RoleCode AND x.MenuItemKey = 'accounting')");

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7C2E4A91-3B5D-4F68-9A17-C0D2E4F6A801", "ADMIN", "accountingItems" },
                    { "8D3F5BA2-4C6E-4079-AB28-D1E3F507B912", "ADMIN", "distributionRules" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.Sql(
                "DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ('accounting','accountingItems','distributionRules')");

            // Volta Plano de Contas e Centro de Custo a Cadastros com as ordens originais.
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET ParentKey = 'registers', [Order] = 12, Title = 'Contas Contábeis' WHERE [Key] = 'ledgerAccounts'");
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET ParentKey = 'registers', [Order] = 11, Title = 'Centros de Custo' WHERE [Key] = 'costCenters'");

            // Filhos antes do pai, pela auto-relação ParentKey.
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] IN ('accountingItems','distributionRules')");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] = 'accounting'");

            migrationBuilder.Sql("UPDATE MENU_ITEMS SET [Order] = [Order] - 1 WHERE ParentKey IS NULL AND [Order] >= 9");
        }
    }
}
