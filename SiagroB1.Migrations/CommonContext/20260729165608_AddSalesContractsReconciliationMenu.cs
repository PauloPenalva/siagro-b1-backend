using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da tela de conciliação de saldos de contrato de venda, no grupo "Vendas".
    /// É o caminho que permite mover volume faturado para um contrato SEM liberação de
    /// entrega e deixando o destino negativo — por isso fica numa rota própria, liberada
    /// apenas para a role ADMIN, separada da realocação operacional do dia a dia
    /// ("salesContractsAllocations").
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
    /// item não aparece para ninguém.
    /// </summary>
    public partial class AddSalesContractsReconciliationMenu : Migration
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
                        "salesContractsReconciliation", "Conciliação de Saldos de Venda",
                        "sap-icon://journey-change", true, false, 11, "sales"
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
                        "8B1D2A47-3F6C-4E58-9A70-5C2E9D4F1B36", "ADMIN",
                        "salesContractsReconciliation"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    "8B1D2A47-3F6C-4E58-9A70-5C2E9D4F1B36"
                });

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: new object[]
                {
                    "salesContractsReconciliation"
                });
        }
    }
}
