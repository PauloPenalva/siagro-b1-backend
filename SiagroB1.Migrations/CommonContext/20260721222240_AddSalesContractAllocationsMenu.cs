using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da tela de alocações de contrato de venda ("Entregas de Contratos de Venda"),
    /// no grupo "Vendas". Permite realocar o volume das invoices entre contratos para
    /// conciliar com o relatório de entrega do cliente.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
    /// item não aparece para ninguém.
    /// </summary>
    public partial class AddSalesContractAllocationsMenu : Migration
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
                        "salesContractsAllocations", "Entregas de Contratos de Venda",
                        "sap-icon://folder-blank", true, false, 10, "sales"
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
                        "C3F5E0D4-9D6A-4C3B-9E78-4A0F2B7D9C53", "ADMIN",
                        "salesContractsAllocations"
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
                    "C3F5E0D4-9D6A-4C3B-9E78-4A0F2B7D9C53"
                });

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: new object[]
                {
                    "salesContractsAllocations"
                });
        }
    }
}
