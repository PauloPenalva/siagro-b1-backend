using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class RenameNfeExportMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "nfeExport",
                column: "Title", value: "Exportar XML/DANFE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "nfeExport",
                column: "Title", value: "Exportar XML/DANFE para o Contador");
        }
    }
}
