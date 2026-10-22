using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// O módulo Contabilidade nasceu com ícone próprio; todo menu superior usa sap-icon://folder-blank, como os demais.
    /// </summary>
    public partial class FixAccountingModuleMenuIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET Icon = 'sap-icon://folder-blank' WHERE [Key] = 'accounting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET Icon = 'sap-icon://accounting-document-verification' WHERE [Key] = 'accounting'");
        }
    }
}
