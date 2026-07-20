using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da entrada de compra em armazenagem própria. Fica no grupo "Compras":
    /// a operação baixa contrato de compra, mesmo terminando na armazenagem.
    /// Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    /// </summary>
    public partial class AddStorageEntryTransactionMenu : Migration
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
                        "storageEntryTransaction", "Entrada em Armazenagem",
                        "sap-icon://folder-blank", true, false, 7, "purchases"
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
                        "5D2F4A61-3C8E-4B77-9E10-7F2C6A5D9B34", "ADMIN",
                        "storageEntryTransaction"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "5D2F4A61-3C8E-4B77-9E10-7F2C6A5D9B34");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "storageEntryTransaction");
        }
    }
}
