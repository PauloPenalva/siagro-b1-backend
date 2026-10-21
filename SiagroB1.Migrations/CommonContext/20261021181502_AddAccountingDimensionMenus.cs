using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddAccountingDimensionMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: dimensões, regras de distribuição e parâmetros só existem em STANDALONE.
            // Orders 12 a 14: logo após Centros de Custo (11); os irmãos de Order >= 12 deslocam 3 posições.
            ShiftSiblings(migrationBuilder, shifted: true);

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "accountingDimensions", "Dimensões Gerenciais", "sap-icon://tree", true, false, 12, "registers", true },
                    { "distributionRules", "Regras de Distribuição", "sap-icon://split", true, false, 13, "registers", true },
                    { "companySettings", "Parâmetros da Empresa", "sap-icon://action-settings", true, false, 14, "registers", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "FFC71853-2854-4F99-9D86-09B02EBCCAAB", "ADMIN", "accountingDimensions" },
                    { "EF615425-B88F-4C01-A512-8C1A34A37495", "ADMIN", "distributionRules" },
                    { "D90FBB5D-8297-4610-9058-DC47947E0A90", "ADMIN", "companySettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "FFC71853-2854-4F99-9D86-09B02EBCCAAB");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "EF615425-B88F-4C01-A512-8C1A34A37495");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "D90FBB5D-8297-4610-9058-DC47947E0A90");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "accountingDimensions");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "distributionRules");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "companySettings");

            ShiftSiblings(migrationBuilder, shifted: false);
        }

        // Irmãos de "registers" que ficavam depois de costCenters (11), deslocados 3 posições (Up) ou de volta ao Order original (Down).
        private static void ShiftSiblings(MigrationBuilder migrationBuilder, bool shifted)
        {
            (string Key, int Order)[] siblings =
            [
                ("ledgerAccounts", 12),
                ("contractTemplates", 14),
                ("companySignatories", 15),
                ("paymentConditions", 16),
                ("emailSettings", 19),
            ];

            foreach (var (key, order) in siblings)
            {
                migrationBuilder.UpdateData(
                    table: "MENU_ITEMS", keyColumn: "Key", keyValue: key, column: "Order", value: shifted ? order + 3 : order);
            }
        }
    }
}
