using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Romaneios de lote de armazenagem de terceiro e as notas de custódia ativas deles (spec 2026-10-12). Mora no Infra
/// porque a Web (seção do romaneio, guarda) e o Reports (PDF) precisam da MESMA consulta, e o Reports não referencia o
/// Application — mesmo motivo do RoyaltyBalanceQuery.
/// </summary>
public static class StorageCustodyQuery
{
    /// <summary>Recepções e Expedições de lote de terceiro Regular, com o lote e a situação da transferência.</summary>
    public static IQueryable<StorageCustodyTransactionRow> Transactions(AppDbContext ctx) =>
        from t in ctx.StorageTransactions.AsNoTracking()
        join a in ctx.StorageAddresses.AsNoTracking() on t.StorageAddressCode equals a.Code
        where (t.TransactionType == StorageTransactionType.Receipt || t.TransactionType == StorageTransactionType.Shipment)
              && a.OwnershipType == StorageOwnershipType.ThirdParty
              && a.Nature == StorageAddressNature.Regular
        select new StorageCustodyTransactionRow
        {
            Key = t.Key,
            Code = t.Code,
            TransactionType = t.TransactionType,
            TransactionStatus = t.TransactionStatus,
            TransactionDate = t.TransactionDate,
            GrossWeight = t.GrossWeight,
            NetWeight = t.NetWeight,
            TruckCode = t.TruckCode,
            IsOwnershipTransfer = t.OwnershipTransferKey != null || t.IsOwnershipTransfer == "Y",
            TransferCancelled = t.OwnershipTransferKey != null && ctx.OwnershipTransfers.Any(o =>
                o.Key == t.OwnershipTransferKey && o.TransferStatus == OwnershipTransferStatus.Cancelled),
            StorageAddressCode = a.Code!,
            BranchCode = a.BranchCode,
            CardCode = a.CardCode,
            CardName = a.CardName,
            ItemCode = a.ItemCode,
            ItemName = a.ItemName,
            UoM = a.UoM,
            OwnershipType = a.OwnershipType,
            Nature = a.Nature,
            RequiresProcessing = a.RequiresProcessing,
        };

    /// <summary>Documentos de entrada não cancelados ligados aos romaneios dados.</summary>
    public static IQueryable<StorageCustodyDocumentRow> PurchaseDocuments(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.PurchaseInvoices.AsNoTracking()
            .Where(i => i.CustodyStorageTransactionKey != null
                        && transactionKeys.Contains(i.CustodyStorageTransactionKey.Value)
                        && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new StorageCustodyDocumentRow
            {
                TransactionKey = i.CustodyStorageTransactionKey!.Value,
                Role = i.CustodyRole,
                DocumentKey = i.Key,
                IsSalesInvoice = false,
                IssuerType = i.IssuerType,
                InvoiceNumber = i.InvoiceNumber,
                TaxDocumentNumber = i.TaxDocumentNumber,
                TaxDocumentSeries = i.TaxDocumentSeries,
                ChaveNFe = i.ChaveNFe,
                InvoiceStatus = i.InvoiceStatus,
                NfeStatus = i.NfeStatus,
                IssueDate = i.IssueDate,
                Quantity = i.Items.Sum(l => l.Quantity),
                Cfop = i.Items.Select(l => l.Cfop).FirstOrDefault(),
            });

    /// <summary>Documentos de saída não cancelados ligados aos romaneios dados (sempre próprios).</summary>
    public static IQueryable<StorageCustodyDocumentRow> SalesDocuments(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.SalesInvoices.AsNoTracking()
            .Where(i => i.CustodyStorageTransactionKey != null
                        && transactionKeys.Contains(i.CustodyStorageTransactionKey.Value)
                        && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new StorageCustodyDocumentRow
            {
                TransactionKey = i.CustodyStorageTransactionKey!.Value,
                Role = i.CustodyRole,
                DocumentKey = i.Key,
                IsSalesInvoice = true,
                IssuerType = DocumentIssuerType.Own,
                InvoiceNumber = i.InvoiceNumber,
                TaxDocumentNumber = i.TaxDocumentNumber,
                TaxDocumentSeries = i.TaxDocumentSeries,
                ChaveNFe = i.ChaveNFe,
                InvoiceStatus = i.InvoiceStatus ?? InvoiceStatus.Pending,
                NfeStatus = i.NfeStatus,
                IssueDate = i.InvoiceDate,
                Quantity = i.Items.Sum(l => l.Quantity),
                Cfop = i.Items.Select(l => l.Cfop).FirstOrDefault(),
            });

    public static async Task<List<StorageCustodyDocumentRow>> ActiveDocumentsAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        if (transactionKeys.Count == 0)
            return [];

        var purchases = await PurchaseDocuments(ctx, transactionKeys).ToListAsync();
        var sales = await SalesDocuments(ctx, transactionKeys).ToListAsync();
        return [.. purchases, .. sales];
    }
}
