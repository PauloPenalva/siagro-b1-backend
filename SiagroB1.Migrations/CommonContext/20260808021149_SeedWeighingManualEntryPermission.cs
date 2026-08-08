using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class SeedWeighingManualEntryPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: o código pode já ter sido cadastrado à mão pela tela de permissões.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'WEIGHING_MANUAL_ENTRY')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('WEIGHING_MANUAL_ENTRY', 'Digitar o peso manualmente na pesagem');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'WEIGHING_MANUAL_ENTRY';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'WEIGHING_MANUAL_ENTRY';");
        }
    }
}
