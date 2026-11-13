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
    // Leitura de exibição: o banco roda READ COMMITTED com RCSI desligado, então sem WITH (NOLOCK) em TODAS as
    // tabelas a aba "Entregas" trava (timeout de 30 s) atrás de qualquer escrita em andamento, mesmo de outro
    // contrato (produção CEAGUI, 14/10/2026). Todo FROM/JOIN novo precisa do hint (há teste que confere).
    private static readonly string SQL = """
        SELECT DISTINCT
            -- Uma linha por alocação: sem a chave, o DISTINCT fundiria duas alocações iguais no mesmo romaneio.
            PA.[Key] AS AllocationKey,
            ST.BranchCode AS BranchCode,
        	B.BranchName AS BranchName,
            ST.Code AS StorageTransactionCode,
            ST.TransactionDate AS StorageTransactionDate,
            CASE
                 WHEN RGEN.ReleaseKey IS NOT NULL THEN N'Devolução'
                 WHEN RCON.[Key] IS NOT NULL THEN N'Reentrega'
        		 WHEN ST.TransactionType = 8 THEN 'Compra'
        		 WHEN ST.TransactionType = 9 THEN 'Dev.Compra'
        		 WHEN ST.TransactionType = 10 THEN 'Compl.Qtd.'
        		 WHEN ST.TransactionType = 16 THEN 'Quebra Descarga'
        		 WHEN ST.TransactionType = 17 THEN 'Sobra Descarga'
        	END AS StorageTransactionTypeName,
        	PC.Code AS ContractNumber,
            -- GAC-1212: o sinal é o do ledger, como no contrato de venda. O que abate o contrato
            -- (Compra, Compl.Qtd., Sobra Descarga) sai positivo; o que devolve saldo (Dev.Compra,
            -- Quebra Descarga) já está gravado negativo e não é mais invertido.
            PA.Volume AS Volume,
            ST.UnitOfMeasureCode AS  UnitOfMeasureCode,
            ST.WarehouseCode AS  WarehouseCode,
            ST.TruckCode AS TruckCode,
            ST.CardCode AS SupplierCode,
            INV.TaxDocumentNumber AS NotaFiscalVenda,
            CASE
                WHEN RGEN.ReleaseKey IS NOT NULL THEN 'LoadRefusal'
                WHEN RCON.[Key] IS NOT NULL THEN 'ReturnRedelivery'
                WHEN ST.TransactionType = 16 THEN 'DischargeLoss'
                WHEN ST.TransactionType = 17 THEN 'DischargeGain'
                WHEN WRR.[Key] IS NOT NULL THEN 'WarehouseLoss'
                ELSE 'Standard'
            END AS Origin,
            WR.Code AS WarehouseReconciliationCode,
            CASE
                WHEN RGEN.ReleaseKey IS NOT NULL AND RLOAD.Code IS NOT NULL THEN N'Carga ' + RLOAD.Code + N' recusada'
                WHEN RGEN.ReleaseKey IS NOT NULL THEN N'Devolução ao armazém'
                WHEN RCON.[Key] IS NOT NULL AND STGEN.Code IS NOT NULL THEN N'Saída da devolução ' + STGEN.Code
                WHEN RCON.[Key] IS NOT NULL THEN N'Saída da devolução ao armazém'
                ELSE WRRS.Description
            END AS ReasonDescription,
            COALESCE(DINV.TaxDocumentNumber, DINV.InvoiceNumber) AS DischargeInvoiceNumber,
            CASE WHEN RGEN.ReleaseKey IS NOT NULL THEN RLOAD.Code ELSE DLOAD.Code END AS DischargeLoadCode,
            -- GAC-1216: carga da entrega. O romaneio de compra não carrega ShipmentLoadKey: a carga é
            -- a da perna de venda da Expedição; na troca de liberação (GAC-1177) a original sai da
            -- carga e o elo é o documento da troca; na quebra/sobra de descarga, a nota conferida.
            -- GAC-1214: na devolução, a carga recusada; na reentrega, a carga do próprio romaneio.
            CASE
                WHEN RGEN.ReleaseKey IS NOT NULL THEN RLOAD.Code
                WHEN RCON.[Key] IS NOT NULL THEN OLOAD.Code
                ELSE COALESCE(SLOAD.Code, CHGLOAD.Code, DLOAD.Code)
            END AS LoadCode,
            ST.[Key] AS StorageTransactionKey,
            ST.ItemCode AS ItemCode,
            ST.NetWeight AS PhysicalQuantity,
            ST.FiscalReconciledAt AS FiscalReconciledAt,
            FIS.FiscalQuantity AS FiscalQuantity,
            FIS.LinkCount AS FiscalLinkCount,
            SPLIT.ContractCount AS ContractCount
        FROM PURCHASE_CONTRACTS_ALLOCATIONS PA WITH (NOLOCK)
        LEFT JOIN STORAGE_TRANSACTIONS ST WITH (NOLOCK)
               ON ST.[Key] = PA.StorageTransactionKey
        LEFT JOIN PURCHASE_CONTRACTS PC WITH (NOLOCK)
               ON PC.[Key] = PA.PurchaseContractKey
        LEFT JOIN SHIPPING_TRANSACTIONS SHT WITH (NOLOCK)
               ON SHT.PurchaseStorageTransactionKey = ST.[Key]
        LEFT JOIN STORAGE_TRANSACTIONS SALES WITH (NOLOCK)
               ON SALES.[Key] = SHT.SalesStorageTransactionKey
        LEFT JOIN BRANCHS B WITH (NOLOCK)
               ON B.Code = ST.BranchCode
        LEFT JOIN SALES_INVOICES INV WITH (NOLOCK)
               ON INV.[Key] = SALES.SalesInvoiceKey
        -- Compra(8) gerada pela aprovação da Conferência de Saldo de Armazém (GAC-1164): a perda é
        -- custo da empresa e consome a liberação, mas o romaneio continua sendo Compra no contrato.
        LEFT JOIN WAREHOUSE_RECONCILIATION_RELEASES WRR WITH (NOLOCK)
               ON WRR.PurchaseStorageTransactionKey = ST.[Key]
        LEFT JOIN WAREHOUSE_RECONCILIATIONS WR WITH (NOLOCK)
               ON WR.[Key] = WRR.WarehouseReconciliationKey
        LEFT JOIN WAREHOUSE_RECONCILIATION_REASONS WRRS WITH (NOLOCK)
               ON WRRS.[Key] = WR.ReasonKey
        -- Troca nota (romaneios 16/17): a nota de venda conferida e a carga que ela faturou.
        LEFT JOIN SALES_INVOICES_ITEMS DITEM WITH (NOLOCK)
               ON DITEM.[Key] = ST.DischargeSalesInvoiceItemKey
        LEFT JOIN SALES_INVOICES DINV WITH (NOLOCK)
               ON DINV.[Key] = DITEM.SalesInvoiceKey
        LEFT JOIN SHIPMENT_LOADS DLOAD WITH (NOLOCK)
               ON DLOAD.[Key] = DINV.ShipmentLoadKey
        -- GAC-1216: carga em que a perna de venda da Expedição foi montada.
        LEFT JOIN SHIPMENT_LOADS SLOAD WITH (NOLOCK)
               ON SLOAD.[Key] = SALES.ShipmentLoadKey
        -- GAC-1216: troca de liberação — a que tirou este romaneio da carga ou a que o gerou.
        LEFT JOIN SHIPPING_RELEASE_CHANGES CHG WITH (NOLOCK)
               ON CHG.[Key] = COALESCE(ST.ReplacedByShippingReleaseChangeKey, ST.ShippingReleaseChangeKey)
        LEFT JOIN SHIPMENT_LOADS CHGLOAD WITH (NOLOCK)
               ON CHGLOAD.[Key] = CHG.ShipmentLoadKey
        -- GAC-1214: alocações derivadas da liberação de devolução (Origin = 2, SalesReturn).
        -- Reentrega: o romaneio 7/13/12 consome a liberação do contrato desta linha.
        LEFT JOIN SHIPMENT_RELEASES RCON WITH (NOLOCK)
               ON RCON.[Key] = ST.ShipmentReleaseKey
              AND RCON.Origin = 2
              AND RCON.PurchaseContractKey = PA.PurchaseContractKey
              AND ST.TransactionType IN (7, 12, 13)
        LEFT JOIN STORAGE_TRANSACTIONS STGEN WITH (NOLOCK)
               ON STGEN.[Key] = RCON.GeneratedByStorageTransactionKey
        LEFT JOIN SHIPMENT_LOADS OLOAD WITH (NOLOCK)
               ON OLOAD.[Key] = ST.ShipmentLoadKey
        -- Devolução: o romaneio 12 gerou liberação de devolução NESTE contrato. TOP 1 filtrado pelo
        -- contrato da linha: o gerador pode ter liberações em vários contratos (e mais de uma no mesmo),
        -- e um JOIN direto duplicaria ou cruzaria as linhas.
        OUTER APPLY (
            SELECT TOP 1 RG.[Key] AS ReleaseKey
            FROM SHIPMENT_RELEASES RG WITH (NOLOCK)
            WHERE RG.GeneratedByStorageTransactionKey = ST.[Key]
              AND RG.Origin = 2
              AND RG.PurchaseContractKey = PA.PurchaseContractKey
        ) RGEN
        LEFT JOIN SHIPMENT_LOADS RLOAD WITH (NOLOCK)
               ON RLOAD.[Key] = ST.RefusedFromShipmentLoadKey
              AND RGEN.ReleaseKey IS NOT NULL
        -- Conciliação fiscal (spec 2026-10-13 §7): NFs do fornecedor vinculadas ao romaneio (NF cancelada não conta).
        -- ⚠️ Sem STRING_AGG aqui: dentro deste SELECT DISTINCT com 11 LEFT JOINs ele levou o plano de 0,3 s a mais de
        -- 40 s (CEAGUI, 13/10/2026). Os números das NFs vêm de LinksSql, uma consulta à parte.
        OUTER APPLY (
            SELECT SUM(PFR.Quantity) AS FiscalQuantity,
                   COUNT(*) AS LinkCount
            FROM PURCHASE_FISCAL_RECONCILIATIONS PFR WITH (NOLOCK)
            JOIN PURCHASE_INVOICES_ITEMS PII WITH (NOLOCK) ON PII.[Key] = PFR.PurchaseInvoiceItemKey
            JOIN PURCHASE_INVOICES PI WITH (NOLOCK) ON PI.[Key] = PII.PurchaseInvoiceKey
            WHERE PFR.StorageTransactionKey = ST.[Key] AND PI.InvoiceStatus <> 2
        ) FIS
        OUTER APPLY (
            SELECT COUNT(DISTINCT PA2.PurchaseContractKey) AS ContractCount
            FROM PURCHASE_CONTRACTS_ALLOCATIONS PA2 WITH (NOLOCK)
            WHERE PA2.StorageTransactionKey = ST.[Key]
        ) SPLIT
        WHERE PA.PurchaseContractKey = @purchaseContractKey
        """;
    
    /// <summary>NFs do fornecedor vinculadas aos romaneios do contrato (NF cancelada não conta).</summary>
    private static readonly string LinksSql = """
        SELECT DISTINCT PFR.StorageTransactionKey AS StorageTransactionKey,
               PI.TaxDocumentNumber AS TaxDocumentNumber,
               PI.TaxDocumentSeries AS TaxDocumentSeries,
               PI.InvoiceNumber AS InvoiceNumber
        FROM PURCHASE_FISCAL_RECONCILIATIONS PFR WITH (NOLOCK)
        JOIN PURCHASE_INVOICES_ITEMS PII WITH (NOLOCK) ON PII.[Key] = PFR.PurchaseInvoiceItemKey
        JOIN PURCHASE_INVOICES PI WITH (NOLOCK) ON PI.[Key] = PII.PurchaseInvoiceKey
        JOIN PURCHASE_CONTRACTS_ALLOCATIONS PA WITH (NOLOCK) ON PA.StorageTransactionKey = PFR.StorageTransactionKey
        WHERE PA.PurchaseContractKey = @purchaseContractKey AND PI.InvoiceStatus <> 2
        """;

    public async Task<ICollection<PurchaseContractAllocationsByContractDto>> ExecuteAsync(Guid purchaseContractKey)
    {
        var allocations = new List<PurchaseContractAllocationsByContractDto>();
        
        var contractAllocations = 
            dbConnection.Query(SQL,  new { purchaseContractKey })
            .ToList();

        var invoiceNumbers = dbConnection.Query(LinksSql, new { purchaseContractKey })
            .GroupBy(l => (Guid)l.StorageTransactionKey)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(l => PurchaseFiscalReconciliationRules.TaxDocument(
                        (string?)l.TaxDocumentNumber, (string?)l.TaxDocumentSeries, (string?)l.InvoiceNumber))
                    .Distinct().Order(StringComparer.Ordinal)));

        foreach (var allocation in contractAllocations)
        {
            var supplierName = await GetSupplierName(allocation.CardCode);
            var warehouseName = await GetSupplierName(allocation.WarehouseCode);
            var physical = (decimal?)allocation.PhysicalQuantity ?? 0m;
            var fiscal = (decimal?)allocation.FiscalQuantity ?? 0m;
            var origin = (string?)allocation.Origin;
            var isSalesReturn = origin is "LoadRefusal" or "ReturnRedelivery";

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
                LoadCode = allocation.LoadCode,
                StorageTransactionKey = (Guid?)allocation.StorageTransactionKey,
                ItemCode = (string?)allocation.ItemCode,
                SupplierInvoiceNumbers = allocation.StorageTransactionKey is Guid txKey ? invoiceNumbers.GetValueOrDefault(txKey) : null,
                PhysicalQuantity = physical,
                FiscalQuantity = fiscal,
                FiscalDifference = fiscal - physical,
                // GAC-1214: devolução e reentrega não são compra do fornecedor — sem conciliação fiscal.
                FiscalStatus = isSalesReturn ? null : PurchaseFiscalReconciliationRules.StatusName(PurchaseFiscalReconciliationRules.Status(
                    physical, fiscal, ((int?)allocation.FiscalLinkCount ?? 0) > 0, allocation.FiscalReconciledAt != null)),
                IsSplitAcrossContracts = ((int?)allocation.ContractCount ?? 0) > 1,
            });
        }

        return allocations
            .OrderBy(a => a.StorageTransactionDate)
            .ThenBy(a => a.StorageTransactionCode, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<string?> GetSupplierName(string cardCode)
    {
        var model = await businessPartnerService.GetByIdAsync(cardCode);
        return model?.CardName;
    }
}