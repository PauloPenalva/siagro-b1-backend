using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Regras e consultas da remessa para depósito em armazém de terceiro (spec 2026-10-10), num lugar só para os
/// serviços novos e para as guardas plantadas nos fluxos existentes (liberação, documento de saída, documento de
/// entrada, romaneio). Consultas devolvem IQueryable para o teste conferir a tradução no SQL Server.
/// </summary>
public static class StorageDepositRules
{
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    /// <summary>Remessas não canceladas (Pendente e Confirmada contam: duas abertas remeteriam o dobro).</summary>
    public static IQueryable<SalesInvoice> ActiveRemittances(AppDbContext ctx) =>
        ctx.SalesInvoices.Where(i => i.ShipmentReleaseKey != null && i.InvoiceStatus != InvoiceStatus.Cancelled);

    public static Task<SalesInvoice?> FindActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey) =>
        ActiveRemittances(ctx).Include(i => i.Items).FirstOrDefaultAsync(i => i.ShipmentReleaseKey == shipmentReleaseKey);

    /// <summary>
    /// Romaneios que representam CARREGAMENTO da liberação — os candidatos à reconciliação. Mesmo eixo do
    /// ShipmentReleasesRecalculateShippedService, só com os tipos que somam: Compra(8) na
    /// liberação com perna de compra, Saída(7) na sem. Fica fora a Compra gerada pela Conferência de Armazém
    /// (TransactionOrigin = WarehouseReconciliation): é perda de papel, não caminhão, e a falta é quebra técnica (D3).
    /// A Perda(13) também fica fora, pelo mesmo motivo. Cancelado não conta.
    /// </summary>
    public static IQueryable<StorageTransaction> ShippedTransactions(AppDbContext ctx, Guid shipmentReleaseKey, ReleaseOrigin origin)
    {
        var query = ctx.StorageTransactions.Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                                                       && t.TransactionStatus != StorageTransactionsStatus.Cancelled);

        return ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)
            ? query.Where(t => t.TransactionType == StorageTransactionType.SalesShipment)
            : query.Where(t => t.TransactionType == StorageTransactionType.Purchase
                               && (t.TransactionOrigin == null || t.TransactionOrigin != TransactionCode.WarehouseReconciliation));
    }

    /// <summary>Linhas de retorno não canceladas que apontam para as linhas de remessa dadas.</summary>
    public static IQueryable<PurchaseInvoiceItem> ReturnLinesQuery(
        AppDbContext ctx, IReadOnlyCollection<Guid> remittanceItemKeys, Guid? excludingPurchaseInvoiceKey) =>
        ctx.PurchaseInvoicesItems.Where(l => l.SalesInvoiceItemKey != null
                                             && remittanceItemKeys.Contains(l.SalesInvoiceItemKey.Value)
                                             && l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled
                                             && l.PurchaseInvoiceKey != excludingPurchaseInvoiceKey);

    public static Task<Dictionary<Guid, decimal>> ReturnedByRemittanceItemAsync(
        AppDbContext ctx, IReadOnlyCollection<Guid> remittanceItemKeys, Guid? excludingPurchaseInvoiceKey) =>
        ReturnLinesQuery(ctx, remittanceItemKeys, excludingPurchaseInvoiceKey)
            .AsNoTracking()
            .GroupBy(l => l.SalesInvoiceItemKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    public static async Task EnsureNoActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey, string action)
    {
        var remittance = await ActiveRemittances(ctx).AsNoTracking()
            .Where(i => i.ShipmentReleaseKey == shipmentReleaseKey)
            .Select(i => new { i.TaxDocumentNumber, i.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (remittance != null)
            throw new ApplicationException(
                $"A liberação tem a remessa para depósito nº {DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber)} " +
                $"ativa. Cancele a remessa antes de {action} a liberação.");
    }

    /// <summary>
    /// Romaneio reconciliado não sai de cena (D8): estorná-lo deixaria o retorno apontando um carregamento que não
    /// existe mais. Vale também para a troca de liberação, que estorna sem passar pelo cancelamento comum.
    /// </summary>
    public static async Task EnsureTransactionsNotReconciledAsync(AppDbContext ctx, IEnumerable<Guid> storageTransactionKeys)
    {
        var keys = storageTransactionKeys.ToList();
        if (keys.Count == 0)
            return;

        var hit = await ctx.StorageReturnReconciliations.AsNoTracking()
            .Where(r => keys.Contains(r.StorageTransactionKey))
            .Select(r => new
            {
                r.StorageTransaction!.Code,
                r.PurchaseInvoice!.TaxDocumentNumber,
                r.PurchaseInvoice.InvoiceNumber,
            })
            .FirstOrDefaultAsync();

        if (hit != null)
            throw new ApplicationException(
                $"O romaneio {hit.Code} está reconciliado com o retorno de depósito nº " +
                $"{DocumentNumber(hit.TaxDocumentNumber, hit.InvoiceNumber)}. Desfaça a reconciliação antes.");
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Spec §3.2: o documento de saída e o papel da natureza andam juntos.</summary>
    public static void EnsureSalesRoles(SalesInvoice invoice, IReadOnlyList<SalesInvoiceItemUsage> resolved)
    {
        foreach (var (_, usage) in resolved)
        {
            if (usage.StorageDepositRole == StorageDepositRole.Return)
                throw new DefaultException(
                    $"Natureza {usage.Name} é de retorno de depósito e não pode ser usada no documento de saída.");

            if (usage.StorageDepositRole == StorageDepositRole.Remittance && invoice.ShipmentReleaseKey is null)
                throw new DefaultException(
                    $"Natureza {usage.Name} é de remessa para depósito: emita pela liberação (Emitir remessa).");
        }

        if (invoice.ShipmentReleaseKey != null &&
            (resolved.Count != invoice.Items.Count || resolved.Any(r => r.Usage.StorageDepositRole != StorageDepositRole.Remittance)))
            throw new DefaultException("Remessa para depósito só aceita natureza de remessa para depósito.");
    }

    /// <summary>
    /// A remessa Pendente pode ser editada pela tela do Documento de Saída; na confirmação ela precisa continuar
    /// sendo a remessa da liberação: uma linha, quantidade liberada, destinatário = armazém, liberação viva.
    /// </summary>
    public static async Task EnsureRemittanceMatchesReleaseAsync(AppDbContext ctx, SalesInvoice invoice)
    {
        if (invoice.ShipmentReleaseKey is not { } releaseKey)
            return;

        var release = await ctx.ShipmentReleases.AsNoTracking().FirstOrDefaultAsync(r => r.Key == releaseKey)
                      ?? throw new DefaultException("Liberação da remessa para depósito não encontrada.");

        if (release.Status == ReleaseStatus.Cancelled)
            throw new DefaultException("A liberação desta remessa para depósito está cancelada.");

        var lines = invoice.Items.Count > 0
            ? invoice.Items.ToList()
            : await ctx.SalesInvoicesItems.AsNoTracking().Where(i => i.SalesInvoiceKey == invoice.Key).ToListAsync();

        if (lines.Count != 1 || lines[0].Quantity != release.ReleasedQuantity || invoice.CardCode != release.DeliveryLocationCode)
            throw new DefaultException(
                $"A remessa para depósito precisa ter uma linha com a quantidade liberada " +
                $"({release.ReleasedQuantity.ToString("N3", PtBr)}) para o armazém {release.DeliveryLocationCode}.");
    }

    /// <summary>Número da NF-e quando existe; senão o número interno do documento.</summary>
    public static string DocumentNumber(string? taxDocumentNumber, string? invoiceNumber) =>
        string.IsNullOrWhiteSpace(taxDocumentNumber) ? invoiceNumber ?? string.Empty : taxDocumentNumber;
}
