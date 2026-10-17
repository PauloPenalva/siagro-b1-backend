using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class SeedIcmsStateSettingsSp : Migration
    {
        /// <summary>
        /// Semeia SP no ICMS por UF (spec 2026-10-17 §1): alíquota interna 18 %, FCP 0 e base dupla (DifalBaseRule = 1).
        /// Idempotente: só entra se SP ainda não existir, então o que o usuário já ajustou não é tocado.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO ICMS_STATE_SETTINGS (StateCode, InternalRate, FcpRate, DifalBaseRule)
                SELECT 'SP', 18, 0, 1
                WHERE NOT EXISTS (SELECT 1 FROM ICMS_STATE_SETTINGS WHERE StateCode = 'SP');
                """);
        }

        /// <summary>Sem volta: apagar SP poderia atingir um cadastro que o usuário já ajustou.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
