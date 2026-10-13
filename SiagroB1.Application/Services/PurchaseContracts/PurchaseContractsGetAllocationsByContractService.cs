using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Queries;
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
        		 WHEN ST.TransactionType = 10 THEN 'Compl.Qtd.'
        		 WHEN ST.TransactionType = 16 THEN 'Quebra Descarga'
        		 WHEN ST.TransactionType = 17 THEN 'Sobra Descarga'
        	END AS StorageTransactionTypeName,
        	PC.Code AS ContractNumber,
        	CASE
                WHEN ST.TransactionType = 8 THEN PA.Volume
                WHEN ST.TransactionType = 9 THEN PA.Volume * -1
                WHEN ST.TransactionType = 10 THEN PA.Volume
                -- Troca nota: a alocação da quebra já é negativa; exibida positiva com o tipo dizendo
                -- o sentido, como a Dev.Compra.
                WHEN ST.TransactionType = 16 THEN PA.Volume * -1
                WHEN ST.TransactionType = 17 THEN PA.Volume
            END AS Volume,
            ST.UnitOfMeasureCode AS  UnitOfMeasureCode,
            ST.WarehouseCode AS  WarehouseCode,
            ST.TruckCode AS TruckCode,
            ST.CardCode AS SupplierCode,
            INV.TaxDocumentNumber AS NotaFiscalVenda,
            CASE
                WHEN ST.TransactionType = 16 THEN 'DischargeLoss'
                WHEN ST.TransactionType = 17 THEN 'DischargeGain'
                WHEN WRR.[Key] IS NOT NULL THEN 'WarehouseLoss'
                ELSE 'Standard'
            END AS Origin,
            WR.Code AS WarehouseReconciliationCode,
            WRRS.Description AS ReasonDescription,
            COALESCE(DINV.TaxDocumentNumber, DINV.InvoiceNumber) AS DischargeInvoiceNumber,
            DLOAD.Code AS DischargeLoadCode,
            ST.[Key] AS StorageTransactionKey,
            ST.ItemCode AS ItemCode,
            ST.NetWeight AS PhysicalQuantity,
            ST.FiscalReconciledAt AS FiscalReconciledAt,
            FIS.FiscalQuantity AS FiscalQuantity,
            FIS.LinkCount AS FiscalLinkCount,
            FIS.SupplierInvoiceNumbers AS SupplierInvoiceNumbers,
            SPLIT.ContractCount AS ContractCount
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
        -- Troca nota (romaneios 16/17): a nota de venda conferida e a carga que ela faturou.
        LEFT JOIN SALES_INVOICES_ITEMS DITEM
               ON DITEM.[Key] = ST.DischargeSalesInvoiceItemKey
        LEFT JOIN SALES_INVOICES DINV
               ON DINV.[Key] = DITEM.SalesInvoiceKey
        LEFT JOIN SHIPMENT_LOADS DLOAD
               ON DLOAD.[Key] = DINV.ShipmentLoadKey
        -- Conciliação fiscal (spec 2026-10-13 §7): NFs do fornecedor vinculadas ao romaneio (NF cancelada não conta).
        OUTER APPLY (
            SELECT SUM(PFR.Quantity) AS FiscalQuantity,
                   COUNT(*) AS LinkCount,
                   STRING_AGG(CASE WHEN PI.TaxDocumentNumber IS NULL OR PI.TaxDocumentNumber = ''
                                   THEN PI.InvoiceNumber
                                   ELSE PI.TaxDocumentNumber + COALESCE('-' + NULLIF(PI.TaxDocumentSeries, ''), '') END, ', ')
                       AS SupplierInvoiceNumbers
            FROM PURCHASE_FISCAL_RECONCILIATIONS PFR
            JOIN PURCHASE_INVOICES_ITEMS PII ON PII.[Key] = PFR.PurchaseInvoiceItemKey
            JOIN PURCHASE_INVOICES PI ON PI.[Key] = PII.PurchaseInvoiceKey
            WHERE PFR.StorageTransactionKey = ST.[Key] AND PI.InvoiceStatus <> 2
        ) FIS
        OUTER APPLY (
            SELECT COUNT(DISTINCT PA2.PurchaseContractKey) AS ContractCount
            FROM PURCHASE_CONTRACTS_ALLOCATIONS PA2
            WHERE PA2.StorageTransactionKey = ST.[Key]
        ) SPLIT
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
            var physical = (decimal?)allocation.PhysicalQuantity ?? 0m;
            var fiscal = (decimal?)allocation.FiscalQuantity ?? 0m;
            
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
                ReasonDescription = allocation.ReasonDescription,
                DischargeInvoiceNumber = allocation.DischargeInvoiceNumber,
                DischargeLoadCode = allocation.DischargeLoadCode,
                StorageTransactionKey = (Guid?)allocation.StorageTransactionKey,
                ItemCode = (string?)allocation.ItemCode,
                SupplierInvoiceNumbers = (string?)allocation.SupplierInvoiceNumbers,
                PhysicalQuantity = physical,
                FiscalQuantity = fiscal,
                FiscalDifference = fiscal - physical,
                FiscalStatus = PurchaseFiscalReconciliationRules.StatusName(PurchaseFiscalReconciliationRules.Status(
                    physical, fiscal, ((int?)allocation.FiscalLinkCount ?? 0) > 0, allocation.FiscalReconciledAt != null)),
                IsSplitAcrossContracts = ((int?)allocation.ContractCount ?? 0) > 1,
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