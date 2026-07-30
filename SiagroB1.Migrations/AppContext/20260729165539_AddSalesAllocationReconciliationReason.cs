using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Justificativa do ajuste manual de conciliação (Origin = Reconciliation = 4), que é
    /// o único caminho autorizado a deixar o saldo do contrato de destino negativo.
    /// Gravada nas duas pontas do par −/+ e exibida na tela de entregas do contrato.
    /// Nullable sem backfill de propósito: as linhas já existentes são de outras origens.
    /// </summary>
    public partial class AddSalesAllocationReconciliationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReconciliationReason",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                type: "VARCHAR(500)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReconciliationReason",
                table: "SALES_CONTRACTS_ALLOCATIONS");
        }
    }
}
