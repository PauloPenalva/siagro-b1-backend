using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Encontro de contas entre a DEVOLUÇÃO e o firme normal (spec 2026-10-13, títulos firmes, regras 18–21): uma baixa
/// <see cref="FinancialSettlementOrigin.Netting"/> em CADA título, sem conta, pelo menor dos dois saldos, cada uma com
/// <c>RelatedDocumentKey</c> apontando a outra ponta. Estornar uma das duas estorna o par
/// (FinancialDocumentsReverseSettlementService).
/// </summary>
public class FinancialDocumentsNettingService(IUnitOfWork db)
{
    /// <summary>
    /// Regras 18–19, ENQUEUE-ONLY (a devolução acabou de nascer no mesmo SaveChanges): compensa os firmes nascidos de
    /// vínculos da linha de origem, SOLTOS e com saldo, do mais antigo (CreatedAt, depois RowId). Sem origem, ou origem
    /// composta/quitada, a devolução fica em aberto para o operador.
    /// </summary>
    public async Task EnqueueAutoAsync(FinancialDocument returnDocument, Guid? originLineKey, string userName)
    {
        if (originLineKey is null || returnDocument.Nature != FinancialDocumentNature.PurchaseReturn)
            return;

        var context = db.Context;
        var originLinkKeys = await context.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItemKey == originLineKey.Value)
            .Select(l => l.Key)
            .ToListAsync();
        if (originLinkKeys.Count == 0)
            return;

        var origins = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.Nature == FinancialDocumentNature.Firm &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.OriginKey != null && originLinkKeys.Contains(d.OriginKey.Value))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.RowId)
            .ToListAsync();

        foreach (var firm in origins)
        {
            if (returnDocument.OpenAmount <= 0m)
                break;

            // O SQL filtra Canceled, mas o EF devolve a instância RASTREADA: a origem pode ter sido cancelada neste
            // mesmo SaveChanges (vínculo de origem desfeito junto com o da devolução) — revisão final I2.
            if (firm.Status == FinancialDocumentStatus.Canceled)
                continue;

            // D12: moeda diferente também fica para o operador.
            if (firm.OpenAmount <= 0m || firm.Currency != returnDocument.Currency ||
                PurchaseFirmPayableRules.ProvisionalOf(context, firm.Key) is not null)
                continue;

            await EnqueuePairAsync(returnDocument, firm, Math.Min(returnDocument.OpenAmount, firm.OpenAmount), userName);
        }
    }

    /// <summary>Regra 20: "Compensar com firme" — a devolução em aberto contra um firme solto do mesmo contrato.</summary>
    public async Task ExecuteAsync(Guid returnKey, Guid firmKey, string userName)
    {
        var context = db.Context;

        var refund = await context.FinancialDocuments.FirstOrDefaultAsync(d => d.Key == returnKey)
                     ?? throw new NotFoundException("Documento financeiro não encontrado.");
        if (!refund.IsPurchaseFirmPayable || refund.Nature != FinancialDocumentNature.PurchaseReturn)
            throw new ApplicationException("Compensar com firme só vale para título de devolução.");

        var firm = await context.FinancialDocuments.FirstOrDefaultAsync(d => d.Key == firmKey)
                   ?? throw new NotFoundException("Documento financeiro não encontrado.");
        if (!firm.IsPurchaseFirmPayable || firm.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException($"O título {firm.Code} não é um título firme da conciliação fiscal.");

        if (firm.PurchaseContractKey != refund.PurchaseContractKey)
            throw new ApplicationException($"O título {firm.Code} é de outro contrato.");

        // D12: sem variação cambial ainda, só compensa título da mesma moeda.
        if (firm.Currency != refund.Currency)
            throw new ApplicationException(
                $"O título {firm.Code} está em {Currency(firm.Currency)} e a devolução {refund.Code} em {Currency(refund.Currency)}: compensação entre moedas diferentes ainda não é suportada.");

        foreach (var document in new[] { refund, firm })
        {
            if (document.Status == FinancialDocumentStatus.Canceled)
                throw new ApplicationException($"O título {document.Code} está cancelado.");

            if (document.OpenAmount <= 0m)
                throw new ApplicationException($"O título {document.Code} não tem saldo a compensar.");
        }

        if (PurchaseFirmPayableRules.ProvisionalOf(context, firm.Key) is { } provisionalKey)
        {
            var provisionalCode = await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.Key == provisionalKey).Select(d => d.Code).FirstOrDefaultAsync();
            throw new ApplicationException(
                $"O título {firm.Code} está composto no título {provisionalCode}: só firme solto compensa a devolução.");
        }

        await EnqueuePairAsync(refund, firm, Math.Min(refund.OpenAmount, firm.OpenAmount), userName);
        await db.SaveChangesAsync();
    }

    private static string Currency(CurrencyType currency) =>
        FinancialDocumentsContractCancellationGuardService.CurrencySymbol(currency);

    private async Task EnqueuePairAsync(FinancialDocument refund, FinancialDocument firm, decimal amount, string userName)
    {
        var now = DateTime.Now;
        db.Context.FinancialSettlements.AddRange(
            Line(refund, firm, amount, now, userName),
            Line(firm, refund, amount, now, userName));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, refund.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, firm.Key);
    }

    private static FinancialSettlement Line(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime now, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = now.Date,
        Amount = amount,
        Origin = FinancialSettlementOrigin.Netting,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
