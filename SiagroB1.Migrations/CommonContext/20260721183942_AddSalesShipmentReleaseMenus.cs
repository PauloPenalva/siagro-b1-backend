using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus da liberação de entrega de VENDA. Ficam no grupo "Vendas":
    /// - "salesContractsShipmentRelease": lista de contratos de venda a liberar entrega
    ///   (criar liberação a partir do contrato);
    /// - "salesShipmentReleases": lista/ciclo de vida das liberações de entrega de venda.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Chave divergente produz
    /// um item de menu que não leva a lugar nenhum. As telas de nível 2
    /// (salesContractsShipmentReleaseRequest / salesShipmentReleasesDetail) NÃO viram
    /// item de menu — são acessadas de dentro das telas de lista.
    ///
    /// Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    /// </summary>
    public partial class AddSalesShipmentReleaseMenus : Migration
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
                        "salesContractsShipmentRelease", "Contratos de Venda - Liberação de entregas",
                        "sap-icon://folder-blank", true, false, 8, "sales"
                    },
                    {
                        "salesShipmentReleases", "Liberações de Entrega de Contratos de Venda",
                        "sap-icon://folder-blank", true, false, 9, "sales"
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
                        "A1F3C8D2-7B4E-4A19-9C56-2E8D0F5B7A31", "ADMIN",
                        "salesContractsShipmentRelease"
                    },
                    {
                        "B2E4D9C3-8C5F-4B2A-8D67-3F9E1A6C8B42", "ADMIN",
                        "salesShipmentReleases"
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
                    "A1F3C8D2-7B4E-4A19-9C56-2E8D0F5B7A31",
                    "B2E4D9C3-8C5F-4B2A-8D67-3F9E1A6C8B42"
                });

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: new object[]
                {
                    "salesContractsShipmentRelease",
                    "salesShipmentReleases"
                });
        }
    }
}
