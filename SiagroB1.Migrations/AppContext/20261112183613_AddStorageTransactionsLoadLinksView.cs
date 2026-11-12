using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// GAC-1216: view com a carga em que cada romaneio viajou. As listas (Entregas de Contratos de
    /// Compra, Romaneios de Movimentação) filtram e ordenam por ela via
    /// <c>StorageTransaction.LoadLink</c>. Só cria a view: nenhuma tabela ou dado é alterado.
    /// </summary>
    public partial class AddStorageTransactionsLoadLinksView : Migration
    {
        /// <summary>
        /// A carga chega ao romaneio por seis vínculos, nesta ordem de preferência:
        /// montado na carga; devolvido pela recusa dela (inclui a quebra de trajeto); entrada do
        /// transbordo dela; perna de compra cuja perna de venda da Expedição está na carga; perna
        /// de compra de uma troca de liberação (a original sai da carga e o elo é o documento da
        /// troca); quebra/sobra de descarga, pela nota conferida. É a mesma regra da aba Entregas
        /// do contrato de compra (PurchaseContractsGetAllocationsByContractService).
        /// <para>
        /// ⚠️ No máximo UMA linha por romaneio: a navegação é 1:1 e uma segunda linha duplicaria o
        /// romaneio nas listas. Por isso a perna de venda vem de OUTER APPLY TOP 1, e não de JOIN.
        /// </para>
        /// <para>
        /// WITH (NOLOCK) em tudo: é leitura de exibição, e o banco roda READ COMMITTED sem RCSI —
        /// sem o hint as listas travariam atrás de qualquer escrita em carga/nota em andamento.
        /// </para>
        /// <para>
        /// Mudou a regra? Crie OUTRA migration com o CREATE OR ALTER novo; não edite esta.
        /// </para>
        /// </summary>
        public const string ViewSql = """
            CREATE OR ALTER VIEW STORAGE_TRANSACTIONS_LOAD_LINKS AS
            SELECT ST.[Key] AS StorageTransactionKey,
                   L.[Key] AS ShipmentLoadKey,
                   L.Code AS LoadCode
            FROM STORAGE_TRANSACTIONS ST WITH (NOLOCK)
            OUTER APPLY (
                SELECT TOP 1 SALES.ShipmentLoadKey
                FROM SHIPPING_TRANSACTIONS SHT WITH (NOLOCK)
                JOIN STORAGE_TRANSACTIONS SALES WITH (NOLOCK)
                  ON SALES.[Key] = SHT.SalesStorageTransactionKey
                WHERE SHT.PurchaseStorageTransactionKey = ST.[Key]
                  AND SALES.ShipmentLoadKey IS NOT NULL
                ORDER BY SHT.RowId DESC
            ) LEG
            LEFT JOIN SHIPMENT_LOAD_TRANSSHIPMENTS TS WITH (NOLOCK)
                   ON TS.[Key] = ST.ShipmentLoadTransshipmentKey
            LEFT JOIN SHIPPING_RELEASE_CHANGES CHG WITH (NOLOCK)
                   ON CHG.[Key] = COALESCE(ST.ReplacedByShippingReleaseChangeKey, ST.ShippingReleaseChangeKey)
            LEFT JOIN SALES_INVOICES_ITEMS DITEM WITH (NOLOCK)
                   ON DITEM.[Key] = ST.DischargeSalesInvoiceItemKey
            LEFT JOIN SALES_INVOICES DINV WITH (NOLOCK)
                   ON DINV.[Key] = DITEM.SalesInvoiceKey
            JOIN SHIPMENT_LOADS L WITH (NOLOCK)
              ON L.[Key] = COALESCE(
                     ST.ShipmentLoadKey,
                     ST.RefusedFromShipmentLoadKey,
                     TS.ShipmentLoadKey,
                     LEG.ShipmentLoadKey,
                     CHG.ShipmentLoadKey,
                     DINV.ShipmentLoadKey)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS STORAGE_TRANSACTIONS_LOAD_LINKS");
        }
    }
}
