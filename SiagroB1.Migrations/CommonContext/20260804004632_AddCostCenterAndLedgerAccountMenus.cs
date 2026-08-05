using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus dos cadastros de centro de custo e conta contábil, no grupo "Cadastros".
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
    /// item não aparece para ninguém.
    /// </summary>
    public partial class AddCostCenterAndLedgerAccountMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns:
                [
                    "Key",
                    "Title",
                    "Icon",
                    "Enabled",
                    "Expanded",
                    "Order",
                    "ParentKey"
                ],
                values: new object[,]
                {
                    {
                        "costCenters", "Centros de Custo",
                        "sap-icon://folder-blank", true, false, 11, "registers"
                    },
                    {
                        "ledgerAccounts", "Contas Contábeis",
                        "sap-icon://folder-blank", true, false, 12, "registers"
                    },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns:
                [
                    "Id",
                    "RoleCode",
                    "MenuItemKey"
                ],
                values: new object[,]
                {
                    {
                        "3F7A1C58-6B0D-4E92-A14F-2C8D5E7B9016", "ADMIN",
                        "costCenters"
                    },
                    {
                        "A2D9E430-8C51-4B76-9F03-6E1A7B4C2D85", "ADMIN",
                        "ledgerAccounts"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["3F7A1C58-6B0D-4E92-A14F-2C8D5E7B9016", "A2D9E430-8C51-4B76-9F03-6E1A7B4C2D85"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["costCenters", "ledgerAccounts"]);
        }
    }
}
