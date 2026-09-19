using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>
/// Romaneios de quebra/sobra de descarga no período (inclusive o último dia). O contrato vem pela
/// Expedição (SHIPPING_TRANSACTIONS → Compra 8 → alocação), e não pela alocação do próprio romaneio,
/// que some quando ele é cancelado.
/// </summary>
public class PurchaseDischargesListService(IUnitOfWork db)
{
    public async Task<List<PurchaseDischargeListDto>> ExecuteAsync(DateTime dateFrom, DateTime dateTo)
    {
        var ctx = db.Context;
        var fromDate = dateFrom.Date;
        var untilDate = dateTo.Date.AddDays(1);

        var query =
            from st in ctx.StorageTransactions.AsNoTracking()
            where (st.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
                   st.TransactionType == StorageTransactionType.PurchaseDischargeGain) &&
                  st.TransactionDate >= fromDate && st.TransactionDate < untilDate
            join sht in ctx.ShippingTransactions.AsNoTracking()
                on st.DischargeShippingTransactionKey equals (Guid?)sht.Key into shts
            from sht in shts.DefaultIfEmpty()
            join pa in ctx.PurchaseContractsAllocations.AsNoTracking()
                on (sht == null ? null : sht.PurchaseStorageTransactionKey) equals (Guid?)pa.StorageTransactionKey into pas
            from pa in pas.DefaultIfEmpty()
            join pc in ctx.PurchaseContracts.AsNoTracking()
                on (pa == null ? (Guid?)null : pa.PurchaseContractKey) equals (Guid?)pc.Key into pcs
            from pc in pcs.DefaultIfEmpty()
            join item in ctx.SalesInvoicesItems.AsNoTracking()
                on st.DischargeSalesInvoiceItemKey equals item.Key into items
            from item in items.DefaultIfEmpty()
            join inv in ctx.SalesInvoices.AsNoTracking()
                on (item == null ? null : item.SalesInvoiceKey) equals (Guid?)inv.Key into invs
            from inv in invs.DefaultIfEmpty()
            join load in ctx.ShipmentLoads.AsNoTracking()
                on (inv == null ? null : inv.ShipmentLoadKey) equals (Guid?)load.Key into loads
            from load in loads.DefaultIfEmpty()
            orderby st.TransactionDate descending, st.Code descending
            select new PurchaseDischargeListDto
            {
                Key = st.Key,
                Code = st.Code,
                TransactionType = st.TransactionType,
                Status = st.TransactionStatus,
                TransactionDate = st.TransactionDate,
                SupplierCode = st.CardCode,
                SupplierName = st.CardName,
                PurchaseContractKey = pc == null ? null : pc.Key,
                PurchaseContractCode = pc == null ? null : pc.Code,
                ShipmentLoadCode = load == null ? null : load.Code,
                SalesInvoiceNumber = inv == null ? null : (inv.TaxDocumentNumber ?? inv.InvoiceNumber),
                Volume = st.NetWeight,
                UnitOfMeasureCode = st.UnitOfMeasureCode,
                CreatedBy = st.CreatedBy,
            };

        return await query.ToListAsync();
    }
}
