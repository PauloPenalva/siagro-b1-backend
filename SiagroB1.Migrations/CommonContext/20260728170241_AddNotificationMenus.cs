using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus da notificação por WhatsApp, no grupo "Administração": cadastro dos grupos de
    /// destinatários e consulta do log de envio.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
    /// item não aparece para ninguém.
    /// </summary>
    public partial class AddNotificationMenus : Migration
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
                        "notificationGroups", "Grupos de Notificação",
                        "sap-icon://folder-blank", true, false, 11, "admin"
                    },
                    {
                        "notificationLogs", "Log de Notificações",
                        "sap-icon://folder-blank", true, false, 12, "admin"
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
                        "8C4E2A19-D7B3-4F60-9E85-1A2B3C4D5E6F", "ADMIN",
                        "notificationGroups"
                    },
                    {
                        "5B9F7D34-1E62-4A08-B7C1-9D8E0F2A3B4C", "ADMIN",
                        "notificationLogs"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["8C4E2A19-D7B3-4F60-9E85-1A2B3C4D5E6F", "5B9F7D34-1E62-4A08-B7C1-9D8E0F2A3B4C"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["notificationGroups", "notificationLogs"]);
        }
    }
}
