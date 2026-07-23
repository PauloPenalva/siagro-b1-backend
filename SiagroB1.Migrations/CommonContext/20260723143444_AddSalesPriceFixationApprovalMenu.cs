using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da fila de aprovação de fixações de preço de contratos de VENDA a fixar (PAF),
    /// usada pela diretoria para aprovar ou rejeitar. Fica no grupo "Vendas", espelhando
    /// AddPriceFixationApprovalMenu (que fez o mesmo para compras).
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
    /// item não aparece para ninguém.
    /// </summary>
    public partial class AddSalesPriceFixationApprovalMenu : Migration
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
                        "salesContractsPriceFixationApproval", "Aprovação de Fixações de Preço (Venda)",
                        "sap-icon://folder-blank", true, false, 11, "sales"
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
                        "7E2A9D14-3C8B-4A6F-B15E-2D9F0C7B4A83", "ADMIN",
                        "salesContractsPriceFixationApproval"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "7E2A9D14-3C8B-4A6F-B15E-2D9F0C7B4A83");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "salesContractsPriceFixationApproval");
        }
    }
}
