using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Migration intencionalmente vazia (baseline).
    /// O ModelSnapshot estava dessincronizado do Designer da última migration
    /// (refactor do MasterEntity — colunas Code/Name — aplicado ao banco
    /// manualmente, sem migration) e carregava TypeNames malformados
    /// ("DECIMAL(18,p) DEFAULT 0)"). O snapshot foi corrigido; esta migration
    /// existe apenas para registrar um Designer limpo e consistente com o
    /// modelo atual. Nenhuma alteração de schema é necessária: o banco já
    /// reflete este estado.
    /// </summary>
    public partial class SyncSnapshotMasterEntityRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sem operações — o schema do banco já corresponde ao modelo.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem operações — nada a reverter.
        }
    }
}
