using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da fila de aprovação de fixações de preço, usada pela diretoria para
    /// aprovar ou rejeitar fixações de contratos a fixar (PAF). Fica no grupo
    /// "Compras", ao lado da aprovação de contratos.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Chave divergente produz
    /// um item de menu que não leva a lugar nenhum.
    ///
    /// Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    /// </summary>
    public partial class AddPriceFixationApprovalMenu : Migration
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
                        "purchaseContractsPriceFixationApproval", "Aprovação de Fixações de Preço",
                        "sap-icon://folder-blank", true, false, 8, "purchases"
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
                        "9B4E1C72-6A3D-4F58-8C21-0E7B5A9D3F46", "ADMIN",
                        "purchaseContractsPriceFixationApproval"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "9B4E1C72-6A3D-4F58-8C21-0E7B5A9D3F46");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "purchaseContractsPriceFixationApproval");
        }
    }
}
