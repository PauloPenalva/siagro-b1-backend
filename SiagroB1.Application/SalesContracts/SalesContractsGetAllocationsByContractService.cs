using System.Data;
using Dapper;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsGetAllocationsByContractService(
    SapErpDbContext sap,
    IDbConnection dbConnection
    )
{
    private static readonly string SQL = """
        SELECT DISTINCT 
           INV.BranchCode AS BranchCode, 
           B.BranchName AS BranchName,
           INV.InvoiceNumber AS InvoiceNumber,
           INV.InvoiceDate AS InvoiceDate,
           INV.TaxDocumentNumber AS NotaFiscal,
           INV.TaxDocumentSeries AS Serie,
           IIF(INV.InvoiceType = 0, 'Normal', 'Retorno') AS Type,
           IIF(INV.InvoiceType = 0, INVI.Quantity, INVI.Quantity * -1) AS Quantity,
           INVI.UnitOfMeasureCode AS  UnitOfMeasureCode,
           INV.TruckCode AS TruckCode,
           INVI.DeliveredQuantity AS DeliveredQuantity,
           INVI.QuantityLoss AS QuantityLoss,
           IIF(INVI.DeliveryStatus = 0, 'Aberto', 'Encerrado') AS DeliveryStatus
        FROM SALES_INVOICES_ITEMS INVI
        LEFT JOIN SALES_INVOICES INV
         ON INV.[Key] = INVI.SalesInvoiceKey
        LEFT JOIN BRANCHS B
        ON B.Code = INV.BranchCode
        WHERE INVI.SalesContractKey = @SalesContractKey
          AND (INV.InvoiceStatus = 1 OR INV.InvoiceStatus = 3) 
        ORDER BY INV.InvoiceNumber
        """;
    
    public ICollection<SalesContractsGetAllocationsByContractDto> ExecuteAsync(Guid key)
    {
        return dbConnection.Query<SalesContractsGetAllocationsByContractDto>(SQL,  new { SalesContractKey = key })
            .ToList();
    }
    
}