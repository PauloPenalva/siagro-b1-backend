using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorno de romaneio de compra diante dos títulos firmes (spec 2026-10-13, títulos firmes, regras 23–24 e D7).
/// Chamado por PurchaseContractsAllocationDeleteService.ExecuteAsync — o único caminho de todos os estornos — ANTES de
/// qualquer outra mudança. Estático para não mudar o construtor daquele serviço. ENQUEUE-ONLY.
/// </summary>
public static class FinancialDocumentsFirmUnwind
{
    public static async Task EnqueueForAllocationAsync(
        AppDbContext context, StorageTransaction transaction, Guid purchaseContractKey, string userName)
    {
        // Regra 24: devolução (9) e quebra (16) não travam nem desfazem nada — o efeito vem do título de devolução.
        if (transaction.TransactionType is StorageTransactionType.PurchaseReturn or StorageTransactionType.PurchaseDischargeLoss)
            return;

        // Só os vínculos do romaneio com documentos DESTE contrato.
        var links = await context.PurchaseFiscalReconciliations
            .Where(l => l.StorageTransactionKey == transaction.Key &&
                        l.PurchaseInvoiceItem!.PurchaseContractKey == purchaseContractKey)
            .ToListAsync();
        if (links.Count == 0)
            return;

        var linkKeys = links.Select(l => l.Key).ToList();
        var firmKeys = PurchaseFirmPayableRules.ActiveFirmsOf(context, linkKeys).Select(f => f.Key).ToList();
        var compositions = await context.FinancialDocumentCompositions
            .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
            .ToListAsync();

        // D7: o título em que o romaneio compõe já tem baixa ou compensação → recusa.
        var provisionalKeys = compositions.Select(c => c.ProvisionalDocumentKey).Distinct().ToList();
        var paid = await context.FinancialDocuments.AsNoTracking()
            .Where(d => provisionalKeys.Contains(d.Key) && d.SettledAmount != 0m)
            .OrderBy(d => d.Code)
            .Select(d => d.Code)
            .FirstOrDefaultAsync();
        if (paid is not null)
            throw new ApplicationException(
                $"O romaneio {transaction.Code} compõe o título {paid}, que já tem baixa ou compensação: estorne a baixa antes.");

        // Regra 13 sem a checagem (o provisório não tem baixa), remoção dos vínculos e regra 7.
        foreach (var composition in compositions)
            await FinancialDocumentsUncomposeService.EnqueueAsync(context, composition, userName);

        context.PurchaseFiscalReconciliations.RemoveRange(links);
        PurchaseFirmPayableRules.CancelLooseFirms(context, linkKeys, userName);

        // O romaneio volta a "Sem NF".
        transaction.FiscalReconciledAt = null;
        transaction.FiscalReconciledBy = null;
    }
}
