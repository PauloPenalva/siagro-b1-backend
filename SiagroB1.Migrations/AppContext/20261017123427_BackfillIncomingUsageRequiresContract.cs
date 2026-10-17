using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class BackfillIncomingUsageRequiresContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 2026-10-17 §1: a natureza de entrada passa a decidir se o documento exige contrato. As naturezas de
            // entrada que já geram financeiro eram, na prática, do caminho do grão; sem este backfill virariam "outra
            // compra" (Direction 2 = Incoming). Só dado, sem mudar o esquema.
            migrationBuilder.Sql(@"
UPDATE e SET RequiresContract = 1
FROM USAGE_EFFECTS e
JOIN USAGES u ON u.Code = e.UsageCode
WHERE u.Direction = 2 AND u.CreatesFinancialDocument = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
