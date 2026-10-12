using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Consultas de "Remessas para Depósito". ESPELHAM <c>StorageDepositRules</c> da Application, que o Reports não
/// referencia: remessa ativa, retorno não cancelado, romaneio de carregamento por origem e reconciliação.
/// Mudou lá, muda aqui — <c>StorageRemittancesByPeriodReportServiceTests</c> compara com o resumo da liberação.
/// Chaves vão como IQueryable para virar IN (SELECT ...), sem lista de parâmetros.
/// </summary>
public static class StorageRemittanceData
{
    public sealed record ReturnLine(
        Guid RemittanceItemKey, Guid PurchaseInvoiceKey, string? InvoiceNumber, string? TaxDocumentNumber,
        string? TaxDocumentSeries, string? AccessKey, DateTime? IssueDate, InvoiceStatus Status, NfeStatus NfeStatus,
        decimal Quantity);

    public sealed record TransactionLine(
        Guid Key, Guid ReleaseKey, StorageTransactionType Type, TransactionCode? Origin, string? Code,
        DateTime? TransactionDate, string? TruckCode, decimal NetWeight, Guid? ReturnKey, string? ReturnNumber)
    {
        public bool HasReturn => ReturnKey != null;
    }

    /// <summary>Remessas não canceladas (Pendente e Confirmada) emitidas no período, com os filtros opcionais.</summary>
    public static IQueryable<SalesInvoice> RemittancesQuery(AppDbContext context, StorageRemittancesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        var query = context.SalesInvoices.Where(i => i.ShipmentReleaseKey != null
                                                     && i.InvoiceStatus != InvoiceStatus.Cancelled
                                                     && i.InvoiceDate >= from && i.InvoiceDate < toExclusive);

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.Items.Any(l => l.ItemCode == request.ItemCode));

        return query;
    }

    public static IQueryable<Guid> ReleaseKeys(IQueryable<SalesInvoice> remittances) =>
        remittances.Select(i => i.ShipmentReleaseKey!.Value);

    public static IQueryable<Guid> RemittanceItemKeys(IQueryable<SalesInvoice> remittances) =>
        remittances.SelectMany(i => i.Items).Where(l => l.Key != null).Select(l => l.Key!.Value);

    /// <summary>Linhas de retorno não canceladas que apontam para as linhas de remessa dadas.</summary>
    public static IQueryable<ReturnLine> ReturnLinesQuery(AppDbContext context, IQueryable<Guid> remittanceItemKeys) =>
        context.PurchaseInvoicesItems
            .Where(l => l.SalesInvoiceItemKey != null
                        && remittanceItemKeys.Contains(l.SalesInvoiceItemKey.Value)
                        && l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new ReturnLine(
                l.SalesInvoiceItemKey!.Value,
                l.PurchaseInvoiceKey!.Value,
                l.PurchaseInvoice!.InvoiceNumber,
                l.PurchaseInvoice.TaxDocumentNumber,
                l.PurchaseInvoice.TaxDocumentSeries,
                l.PurchaseInvoice.ChaveNFe,
                l.PurchaseInvoice.IssueDate,
                l.PurchaseInvoice.InvoiceStatus,
                l.PurchaseInvoice.NfeStatus,
                l.Quantity));

    /// <summary>
    /// Romaneios não cancelados de Compra(8) e Saída(7) das liberações, com o retorno reconciliado. A escolha
    /// entre os dois tipos depende da origem da liberação e é feita em memória por <see cref="IsShipped"/>.
    /// </summary>
    public static IQueryable<TransactionLine> TransactionsQuery(AppDbContext context, IQueryable<Guid> releaseKeys) =>
        context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey != null
                        && releaseKeys.Contains(t.ShipmentReleaseKey.Value)
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.Purchase))
            .Select(t => new TransactionLine(
                t.Key,
                t.ShipmentReleaseKey!.Value,
                t.TransactionType,
                t.TransactionOrigin,
                t.Code,
                t.TransactionDate,
                t.TruckCode,
                t.NetWeight,
                context.StorageReturnReconciliations
                    .Where(r => r.StorageTransactionKey == t.Key)
                    .Select(r => (Guid?)r.PurchaseInvoiceKey)
                    .FirstOrDefault(),
                context.StorageReturnReconciliations
                    .Where(r => r.StorageTransactionKey == t.Key)
                    .Select(r => r.PurchaseInvoice!.InvoiceNumber)
                    .FirstOrDefault()));

    /// <summary>
    /// Mesmo eixo de <c>StorageDepositRules.ShippedTransactions</c>: Saída(7) na liberação sem perna de compra;
    /// Compra(8) nas demais, menos a gerada pela Conferência de Armazém (perda de papel, não caminhão).
    /// </summary>
    public static bool IsShipped(TransactionLine transaction, ReleaseOrigin origin) =>
        ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)
            ? transaction.Type == StorageTransactionType.SalesShipment
            : transaction.Type == StorageTransactionType.Purchase
              && transaction.Origin != TransactionCode.WarehouseReconciliation;
}
