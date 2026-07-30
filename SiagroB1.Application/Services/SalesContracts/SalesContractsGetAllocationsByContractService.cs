using System.Data;
using Dapper;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Alocações do contrato de venda a partir do ledger SALES_CONTRACTS_ALLOCATIONS (fonte
/// única do consumo): inclui faturamento, realocações (pares −/+), devoluções e backfill,
/// com preços snapshotados e diferença apurada por linha. Alimenta o fragmento de
/// alocações do detalhe do contrato. Linhas de notas canceladas/estornadas não existem no
/// ledger — não há filtro de status aqui.
/// </summary>
public class SalesContractsGetAllocationsByContractService(
    IDbConnection dbConnection
    )
{
    private static readonly string SQL = """
        SELECT
           A.[Key] AS AllocationKey,
           INV.BranchCode AS BranchCode,
           B.BranchName AS BranchName,
           INV.InvoiceNumber AS InvoiceNumber,
           INV.InvoiceDate AS InvoiceDate,
           INV.TaxDocumentNumber AS NotaFiscal,
           INV.TaxDocumentSeries AS Serie,
           IIF(INV.InvoiceType = 0, 'Normal', 'Retorno') AS Type,
           CASE A.Origin
               WHEN 0 THEN 'Faturamento'
               WHEN 1 THEN 'Realocação'
               WHEN 2 THEN 'Devolução'
               WHEN 3 THEN 'Migração'
               WHEN 4 THEN 'Conciliação'
               ELSE '-'
           END AS OriginLabel,
           A.ReconciliationReason AS ReconciliationReason,
           A.Volume AS Quantity,
           INVI.UnitOfMeasureCode AS UnitOfMeasureCode,
           INV.TruckCode AS TruckCode,
           INVI.DeliveredQuantity AS DeliveredQuantity,
           INVI.QuantityLoss AS QuantityLoss,
           IIF(INVI.DeliveryStatus = 0, 'Aberto', 'Encerrado') AS DeliveryStatus,
           A.InvoiceUnitPrice AS InvoiceUnitPrice,
           A.ContractPrice AS ContractPrice,
           A.PriceDifference AS PriceDifference,
           R.RowId AS SalesShipmentReleaseRowId,
           R.ReleaseDate AS SalesShipmentReleaseDate,
           CP.Code AS CounterpartyContractCode
        FROM SALES_CONTRACTS_ALLOCATIONS A
        INNER JOIN SALES_INVOICES_ITEMS INVI
          ON INVI.[Key] = A.SalesInvoiceItemKey
        LEFT JOIN SALES_INVOICES INV
          ON INV.[Key] = INVI.SalesInvoiceKey
        LEFT JOIN BRANCHS B
          ON B.Code = INV.BranchCode
        LEFT JOIN SALES_SHIPMENT_RELEASES R
          ON R.[Key] = A.SalesShipmentReleaseKey
        LEFT JOIN SALES_CONTRACTS CP
          ON CP.[Key] = A.CounterpartySalesContractKey
        WHERE A.SalesContractKey = @SalesContractKey
        ORDER BY INV.InvoiceNumber, A.RowId
        """;

    public ICollection<SalesContractsGetAllocationsByContractDto> ExecuteAsync(Guid key)
    {
        return dbConnection.Query<SalesContractsGetAllocationsByContractDto>(SQL,  new { SalesContractKey = key })
            .ToList();
    }

    private const string TotalsSql = """
        SELECT
            ROUND(ISNULL(SUM(A.Volume * A.ContractPrice), 0), 2) AS TotalDelivered,
            ROUND(ISNULL(SUM(A.Volume * A.InvoiceUnitPrice), 0), 2) AS TotalInvoices,
            ROUND(ISNULL(SUM(A.Volume * A.InvoiceUnitPrice), 0), 2)
              - ROUND(ISNULL(SUM(A.Volume * A.ContractPrice), 0), 2) AS TotalDifference
        FROM SALES_CONTRACTS_ALLOCATIONS A
        WHERE A.SalesContractKey = @SalesContractKey
        """;

    /// <summary>
    /// Totais de reconciliação do contrato (header do detalhe), pelo volume alocado.
    /// </summary>
    public SalesContractAllocationTotalsDto GetTotals(Guid key)
    {
        return dbConnection.QuerySingle<SalesContractAllocationTotalsDto>(
            TotalsSql, new { SalesContractKey = key });
    }

}
