using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus das minutas (fase 1): modelos de contrato e signatários da empresa, no grupo
    /// "Cadastros" (registers). Signatários do parceiro não têm menu — são aba do parceiro.
    /// Order 14 e 15 porque 1 a 13 já estão ocupados em "registers" (13 é Utilizações).
    ///
    /// A Key de cada item PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddContractTemplateMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "contractTemplates", "Modelos de Contrato", "sap-icon://document-text", true, false, 14, "registers" },
                    { "companySignatories", "Signatários da Empresa", "sap-icon://signature", true, false, 15, "registers" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "FCC3A20C-3526-4B0D-8940-C5E3496457B5", "ADMIN", "contractTemplates" },
                    { "2D7EBECA-37B8-4100-ACD1-92ABB6ACBC2F", "ADMIN", "companySignatories" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["FCC3A20C-3526-4B0D-8940-C5E3496457B5", "2D7EBECA-37B8-4100-ACD1-92ABB6ACBC2F"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["contractTemplates", "companySignatories"]);
        }
    }
}
