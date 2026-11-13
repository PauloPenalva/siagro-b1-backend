using Microsoft.EntityFrameworkCore.Migrations;
using SiagroB1.Domain.Enums;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// GAC-1214: as devoluções ao armazém (liberações <see cref="ReleaseOrigin.SalesReturn"/>) criadas
    /// antes da correção não deixaram alocação no contrato de compra, então o saldo devolvido nunca
    /// voltou ao contrato. Gera o mesmo razão que <c>PurchaseContractsReturnReleaseAllocationSync</c>
    /// produziria: cada saída/perda (7/13) viva da liberação consome (+líquido), cada retorno (12)
    /// devolve (−líquido); o romaneio gerador (12, vivo) recebe −ReleasedQuantity se a liberação
    /// está viva, ou −embarcado se Completed/Cancelled. O embarcado é recalculado dos romaneios
    /// vivos (não do <c>ShippedQuantity</c> gravado). Soma por (contrato, romaneio), nunca insere
    /// zero e só insere o que ainda não existe — rodar de novo não muda nada. Por fim refaz o
    /// <c>AllocatedVolume</c> dos contratos que têm liberação de devolução (inclusive encerrados).
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillSalesReturnReleaseAllocations : Migration
    {
        private static readonly int SalesReturn = (int)ReleaseOrigin.SalesReturn;
        private static readonly int ReleaseCompleted = (int)ReleaseStatus.Completed;
        private static readonly int ReleaseCancelled = (int)ReleaseStatus.Cancelled;
        private static readonly int StCancelled = (int)StorageTransactionsStatus.Cancelled;
        private static readonly int Shipment = (int)StorageTransactionType.SalesShipment;
        private static readonly int ShipmentReturn = (int)StorageTransactionType.SalesShipmentReturn;
        private static readonly int Loss = (int)StorageTransactionType.WarehouseLoss;

        private static string RecomputeAllocatedVolume => $@"
UPDATE PC
SET AllocatedVolume = ISNULL((SELECT SUM(A.Volume) FROM PURCHASE_CONTRACTS_ALLOCATIONS A
                              WHERE A.PurchaseContractKey = PC.[Key]), 0)
FROM PURCHASE_CONTRACTS PC
WHERE PC.[Key] IN (SELECT SR.PurchaseContractKey FROM SHIPMENT_RELEASES SR WHERE SR.Origin = {SalesReturn});";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
SET QUOTED_IDENTIFIER ON;

DECLARE @Expected TABLE (PurchaseContractKey UNIQUEIDENTIFIER NOT NULL, StorageTransactionKey UNIQUEIDENTIFIER NOT NULL, Volume DECIMAL(18,3) NOT NULL);

-- (a) consumidores vivos das liberações de devolução: 7/13 consomem, 12 devolve
INSERT INTO @Expected (PurchaseContractKey, StorageTransactionKey, Volume)
SELECT SR.PurchaseContractKey, ST.[Key],
       CASE WHEN ST.TransactionType = {ShipmentReturn} THEN -ST.NetWeight ELSE ST.NetWeight END
FROM STORAGE_TRANSACTIONS ST
INNER JOIN SHIPMENT_RELEASES SR ON SR.[Key] = ST.ShipmentReleaseKey
WHERE SR.Origin = {SalesReturn}
  AND ST.TransactionType IN ({Shipment}, {ShipmentReturn}, {Loss})
  AND ST.TransactionStatus <> {StCancelled}
  AND ST.NetWeight <> 0;

-- (b) gerador vivo: −ReleasedQuantity (liberação viva) ou −embarcado (Completed/Cancelled), por liberação
INSERT INTO @Expected (PurchaseContractKey, StorageTransactionKey, Volume)
SELECT SR.PurchaseContractKey, SR.GeneratedByStorageTransactionKey,
       -(CASE WHEN SR.Status IN ({ReleaseCompleted}, {ReleaseCancelled}) THEN ISNULL(SH.Shipped, 0) ELSE SR.ReleasedQuantity END)
FROM SHIPMENT_RELEASES SR
INNER JOIN STORAGE_TRANSACTIONS G ON G.[Key] = SR.GeneratedByStorageTransactionKey
OUTER APPLY (
    SELECT SUM(CASE WHEN C.TransactionType = {ShipmentReturn} THEN -C.NetWeight ELSE C.NetWeight END) AS Shipped
    FROM STORAGE_TRANSACTIONS C
    WHERE C.ShipmentReleaseKey = SR.[Key]
      AND C.TransactionType IN ({Shipment}, {ShipmentReturn}, {Loss})
      AND C.TransactionStatus <> {StCancelled}
) SH
WHERE SR.Origin = {SalesReturn}
  AND G.TransactionStatus <> {StCancelled};

-- (c) uma linha por (contrato, romaneio), sem zero, só o que ainda não existe
INSERT INTO PURCHASE_CONTRACTS_ALLOCATIONS
    ([Key], PurchaseContractKey, StorageTransactionKey, Volume, ApprovedAt, ApprovedBy, CreatedAt, CreatedBy)
SELECT NEWID(), E.PurchaseContractKey, E.StorageTransactionKey, SUM(E.Volume), GETDATE(), 'migration', GETDATE(), 'migration'
FROM @Expected E
WHERE NOT EXISTS (SELECT 1 FROM PURCHASE_CONTRACTS_ALLOCATIONS A
                  WHERE A.PurchaseContractKey = E.PurchaseContractKey
                    AND A.StorageTransactionKey = E.StorageTransactionKey)
GROUP BY E.PurchaseContractKey, E.StorageTransactionKey
HAVING SUM(E.Volume) <> 0;
{RecomputeAllocatedVolume}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
SET QUOTED_IDENTIFIER ON;

DELETE A
FROM PURCHASE_CONTRACTS_ALLOCATIONS A
WHERE EXISTS (SELECT 1 FROM SHIPMENT_RELEASES SR
              WHERE SR.Origin = {SalesReturn}
                AND SR.PurchaseContractKey = A.PurchaseContractKey
                AND SR.GeneratedByStorageTransactionKey = A.StorageTransactionKey)
   OR EXISTS (SELECT 1 FROM STORAGE_TRANSACTIONS ST
              INNER JOIN SHIPMENT_RELEASES SR ON SR.[Key] = ST.ShipmentReleaseKey
              WHERE ST.[Key] = A.StorageTransactionKey
                AND ST.TransactionType IN ({Shipment}, {ShipmentReturn}, {Loss})
                AND SR.Origin = {SalesReturn}
                AND SR.PurchaseContractKey = A.PurchaseContractKey);
{RecomputeAllocatedVolume}");
        }
    }
}
