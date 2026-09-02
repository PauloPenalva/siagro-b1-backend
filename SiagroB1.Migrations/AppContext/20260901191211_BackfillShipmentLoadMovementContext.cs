using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Preenche cliente e local de entrega nos movimentos de carga JÁ gravados, a partir do
    /// documento de saída que cada um narra.
    /// </summary>
    /// <remarks>
    /// Sem isto, a narrativa de frete só existiria da recusa em diante: toda carga já faturada
    /// mostraria a coluna Cliente em branco justamente nas linhas de <c>Billed</c>, que são as
    /// que dizem para onde a mercadoria foi.
    /// <para>
    /// <c>WHERE</c> estreito e idempotente: só linhas que apontam um documento
    /// (<c>SalesInvoiceKey IS NOT NULL</c>) e que ainda não têm o contexto
    /// (<c>CardCode IS NULL</c>). Rodar de novo não muda nada, e não sobrescreve o que a recusa
    /// gravar depois.
    /// </para>
    /// <para>
    /// <c>Down</c> vazio de propósito: limpar as colunas apagaria também o contexto gravado
    /// pelos movimentos novos, que não vieram deste backfill e não têm outra fonte. A migration
    /// de schema é quem derruba as colunas ao reverter.
    /// </para>
    /// </remarks>
    public partial class BackfillShipmentLoadMovementContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE m
                   SET m.CardCode         = i.CardCode,
                       m.CardName         = i.CardName,
                       m.DeliveryCardCode = i.DeliveryCardCode,
                       m.DeliveryCardName = i.DeliveryCardName
                  FROM SHIPMENT_LOAD_MOVEMENTS m
                  JOIN SALES_INVOICES i ON i.[Key] = m.SalesInvoiceKey
                 WHERE m.SalesInvoiceKey IS NOT NULL
                   AND m.CardCode IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem inverso: ver o <remarks> da classe.
        }
    }
}
