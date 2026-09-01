using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Reclassifica como PLANEJADA (4) a carga legada que ficou ABERTA (0) sem volume nenhum.
    /// </summary>
    /// <remarks>
    /// Antes de <c>ShipmentLoadStatus.Planned</c> existir, o único jeito de criar uma carga era
    /// montá-la a partir de romaneios, então uma carga sem volume só podia ser resquício —
    /// tipicamente uma cujos romaneios foram soltos. Sem esta reclassificação ela continuaria
    /// aparecendo na tela de Faturamento de Expedição, que lista <c>Open</c>, oferecendo ao
    /// usuário uma carga com saldo zero para faturar.
    /// <para>
    /// Não toca em nada com volume, nem em cancelada, nem em faturada: a cláusula é
    /// deliberadamente estreita. Se não houver linha alguma nessa condição — o resultado
    /// esperado numa base saudável — a migration é um no-op.
    /// </para>
    /// </remarks>
    public partial class BackfillShipmentLoadStatusPlanned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE SHIPMENT_LOADS
   SET Status = 4
 WHERE Status = 0
   AND TotalQuantity <= 0;");
        }

        /// <summary>
        /// O inverso é igualmente estreito e igualmente seguro: carga PLANEJADA sem volume volta
        /// a ABERTA, que é exatamente o que ela era antes do <c>Up</c>.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE SHIPMENT_LOADS
   SET Status = 0
 WHERE Status = 4
   AND TotalQuantity <= 0;");
        }
    }
}
