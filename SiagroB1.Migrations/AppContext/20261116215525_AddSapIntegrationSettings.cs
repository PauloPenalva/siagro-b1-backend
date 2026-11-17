using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSapIntegrationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SAP_INTEGRATION_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false),
                    InvoiceSyncEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InvoiceSyncIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    InvoiceSyncLastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SAP_INTEGRATION_SETTINGS", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SAP_INTEGRATION_SETTINGS");
        }
    }
}
