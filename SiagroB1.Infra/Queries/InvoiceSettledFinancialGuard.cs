using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Documento de entrada/saída com título baixado (parcial ou integral) não pode ser excluído nem cancelado. Vale para
/// QUALQUER título ativo ligado ao documento — pela chave do documento, pela linha (firme, complemento de valor) ou pelo
/// vínculo da conciliação fiscal — e roda antes das travas específicas de cada tipo de título, para que a mensagem vista
/// seja sempre a da baixa. No cancelamento da NF-e roda no ensaio, antes de falar com a SEFAZ.
/// </summary>
public static class InvoiceSettledFinancialGuard
{
    public static async Task EnsurePurchaseHasNoSettlementAsync(AppDbContext context, Guid invoiceKey, string? invoiceNumber)
    {
        var lineKeys = await context.PurchaseInvoicesItems.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == invoiceKey && x.Key != null)
            .Select(x => x.Key!.Value)
            .ToListAsync();
        var linkKeys = await context.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(x => lineKeys.Contains(x.PurchaseInvoiceItemKey))
            .Select(x => x.Key)
            .ToListAsync();
        var originKeys = lineKeys.Concat(linkKeys).ToList();

        var titleCode = await context.FinancialDocuments.AsNoTracking()
            .Where(x => x.Status != FinancialDocumentStatus.Canceled && x.SettledAmount != 0m &&
                        (x.PurchaseInvoiceKey == invoiceKey ||
                         (x.OriginKey != null && originKeys.Contains(x.OriginKey.Value))))
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        Refuse(titleCode, invoiceNumber);
    }

    public static async Task EnsureSalesHasNoSettlementAsync(AppDbContext context, Guid invoiceKey, string? invoiceNumber)
    {
        var lineKeys = await context.SalesInvoicesItems.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == invoiceKey && x.Key != null)
            .Select(x => x.Key!.Value)
            .ToListAsync();

        var titleCode = await context.FinancialDocuments.AsNoTracking()
            .Where(x => x.Status != FinancialDocumentStatus.Canceled && x.SettledAmount != 0m &&
                        (x.SalesInvoiceKey == invoiceKey ||
                         (x.OriginKey != null && lineKeys.Contains(x.OriginKey.Value))))
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        Refuse(titleCode, invoiceNumber);
    }

    private static void Refuse(string? titleCode, string? invoiceNumber)
    {
        if (titleCode is null) return;
        throw new DefaultException(
            $"O documento {invoiceNumber} possui baixa financeira (título {titleCode}). Cancelamento não permitido.");
    }
}
