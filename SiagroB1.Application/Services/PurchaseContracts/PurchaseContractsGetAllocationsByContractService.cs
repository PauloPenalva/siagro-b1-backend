using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsGetAllocationsByContractService(
    IBusinessPartnerService businessPartnerService,
    IDbConnection dbConnection
    )
{
    private static readonly string SQL = """
        SELECT DISTINCT 
            ST.BranchCode AS BranchCode,
        	B.BranchName AS BranchName,
            ST.Code AS StorageTransactionCode,
            ST.TransactionDate AS StorageTransactionDate,
            CASE
        		 WHEN ST.TransactionType = 8 THEN 'Compra'
        		 WHEN ST.TransactionType = 9 THEN 'Dev.Compra'
        	END AS StorageTransactionTypeName,
        	PC.Code AS ContractNumber,
        	CASE
                WHEN ST.TransactionType = 8 THEN PA.Volume 
                WHEN ST.TransactionType = 9 THEN PA.Volume * -1     
            END AS Volume, 
            ST.UnitOfMeasureCode AS  UnitOfMeasureCode,
            ST.WarehouseCode AS  WarehouseCode,
            ST.TruckCode AS TruckCode,
            ST.CardCode AS SupplierCode,
            INV.TaxDocumentNumber AS NotaFiscalVenda,
            CASE
                WHEN WRR.[Key] IS NOT NULL THEN 'WarehouseLoss'
                ELSE 'Standard'
            END AS Origin,
            WR.Code AS WarehouseReconciliationCode,
            WRRS.Description AS ReasonDescription
        FROM PURCHASE_CONTRACTS_ALLOCATIONS PA (NOLOCK)
        LEFT JOIN STORAGE_TRANSACTIONS ST
               ON ST.[Key] = PA.StorageTransactionKey
        LEFT JOIN PURCHASE_CONTRACTS PC
               ON PC.[Key] = PA.PurchaseContractKey
        LEFT JOIN SHIPPING_TRANSACTIONS SHT
               ON SHT.PurchaseStorageTransactionKey = ST.[Key]
        LEFT JOIN STORAGE_TRANSACTIONS SALES
               ON SALES.[Key] = SHT.SalesStorageTransactionKey
        LEFT JOIN BRANCHS B
               ON B.Code = ST.BranchCode
        LEFT JOIN SALES_INVOICES INV
               ON INV.[Key] = SALES.SalesInvoiceKey
        -- Compra(8) gerada pela aprovação da Conferência de Saldo de Armazém (GAC-1164): a perda é
        -- custo da empresa e consome a liberação, mas o romaneio continua sendo Compra no contrato.
        LEFT JOIN WAREHOUSE_RECONCILIATION_RELEASES WRR
               ON WRR.PurchaseStorageTransactionKey = ST.[Key]
        LEFT JOIN WAREHOUSE_RECONCILIATIONS WR
               ON WR.[Key] = WRR.WarehouseReconciliationKey
        LEFT JOIN WAREHOUSE_RECONCILIATION_REASONS WRRS
               ON WRRS.[Key] = WR.ReasonKey
        WHERE PA.PurchaseContractKey = @purchaseContractKey
        """;
    
    public async Task<ICollection<PurchaseContractAllocationsByContractDto>> ExecuteAsync(Guid purchaseContractKey)
    {
        var allocations = new List<PurchaseContractAllocationsByContractDto>();
        
        var contractAllocations = 
            dbConnection.Query(SQL,  new { purchaseContractKey })
            .ToList();

        foreach (var allocation in contractAllocations)
        {
            var supplierName = await GetSupplierName(allocation.CardCode);
            var warehouseName = await GetSupplierName(allocation.WarehouseCode);
            
            allocations.Add(new PurchaseContractAllocationsByContractDto
            {
                BranchCode = allocation.BranchCode,
                BranchName = allocation.BranchName,
                StorageTransactionCode = allocation.StorageTransactionCode,
                StorageTransactionDate = allocation.StorageTransactionDate,
                StorageTransactionTypeName = allocation.StorageTransactionTypeName,
                ContractNumber = allocation.ContractNumber,
                Volume = allocation.Volume,
                UnitOfMeasureCode = allocation.UnitOfMeasureCode,
                WarehouseCode = allocation.WarehouseCode,
                WarehouseName = warehouseName,
                TruckCode = allocation.TruckCode,
                SupplierCode = allocation.SupplierCode,
                SupplierName = supplierName,
                NotaFiscalVenda = allocation.NotaFiscalVenda,
                Origin = allocation.Origin,
                WarehouseReconciliationCode = allocation.WarehouseReconciliationCode,
                ReasonDescription = allocation.ReasonDescription
            });
        }

        return allocations;
    }

    private async Task<string?> GetSupplierName(string cardCode)
    {
        var model = await businessPartnerService.GetByIdAsync(cardCode);
        return model?.CardName;
    }
}