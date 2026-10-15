using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Ajusta NO LUGAR o provisório (a pagar ou a receber) de uma fixação cujo volume mudou por washout.
///
/// No lugar, e não cancelar-e-gerar: cancelar-e-gerar exigiria gravar o cancelamento antes do insert (índice único
/// filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin) e buscar número por Dapper no meio da transação.
///
/// Spec 2026-10-13 (títulos firmes, regra 26): o valor não desce abaixo do já COMPOSTO, e o provisório com composição
/// ou baixa não é cancelado. Depois de ajustar, recalcula o status — o washout que cobre a entrega a menos deixa o
/// provisório pago Quitado (D8), e é o que libera o encerramento.
///
/// ENQUEUE-ONLY. Chame ANTES de abrir transação: sem provisório aberto, delega ao gerador, que
/// busca número em DOC_NUMBERS.
/// </summary>
public class FinancialDocumentsAdjustProvisionalService(
    AppDbContext context,
    FinancialDocumentsGenerateService generateService,
    FinancialDocumentChangeLogService changeLog)
{
    public const string WashedOutCancellationReason = "Fixação totalmente consumida por washout";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="remainingVolume">Volume da fixação que continua a faturar, já descontados os washouts aprovados.</param>
    public async Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, decimal remainingVolume, string userName)
    {
        var document = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (document is null)
        {
            // Cancelado por volume zero (e agora estornado) ou dado antigo sem provisório.
            if (remainingVolume > 0)
                await generateService.EnqueueForPurchaseFixationAsync(contract, fixation, userName, remainingVolume);
            return;
        }

        if (remainingVolume <= 0)
        {
            if (document.ComposedAmount != 0m || document.SettledAmount != 0m)
                throw new ApplicationException(
                    $"O washout consumiria toda a fixação, mas o título {document.Code} tem composição ou baixa: desfaça a composição e estorne as baixas antes.");

            FinancialDocumentsCancelService.Cancel(document, WashedOutCancellationReason, userName);
            return;
        }

        var newAmount = decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);
        if (newAmount == document.NetAmount) return;

        if (newAmount < document.ComposedAmount)
            throw new ApplicationException(
                $"O washout reduziria o título {document.Code} para {newAmount.ToString("N2", PtBr)}, abaixo do valor já composto ({document.ComposedAmount.ToString("N2", PtBr)}): desfaça a composição antes.");

        changeLog.Register(
            document.Key,
            FinancialDocumentChangeLogFields.NetAmount,
            document.NetAmount.ToString("N2", PtBr),
            newAmount.ToString("N2", PtBr),
            userName);

        document.NetAmount = newAmount;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
    }

    /// <summary>Spec 2026-10-15 (washout de venda, §5.2).</summary>
    public const string SalesWashedOutCancellationReason = "Volume lavado por washout";

    /// <summary>
    /// Ajuste do provisório a RECEBER da fixação de venda. Diferente da compra numa coisa: com a composição do firme a
    /// receber (sub-projeto A), o novo valor — zero quando o volume zera — não pode ficar abaixo do já composto ou
    /// baixado; a aprovação é recusada (V1) e o operador desfaz a composição antes.
    /// </summary>
    /// <param name="remainingVolume">Volume da fixação que continua a receber, já descontados os washouts aprovados.</param>
    public async Task EnqueueForSalesFixationAsync(
        SalesContract contract, SalesContractPriceFixation fixation, decimal remainingVolume, string userName)
    {
        var document = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (document is null)
        {
            // Cancelado por volume zero (e agora estornado) ou dado antigo sem provisório.
            if (remainingVolume > 0)
                await generateService.EnqueueForSalesFixationAsync(contract, fixation, userName, remainingVolume);
            return;
        }

        var newAmount = remainingVolume <= 0
            ? 0m
            : decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);

        var binding = Math.Max(document.ComposedAmount, document.SettledAmount);
        if (newAmount < binding)
            throw new ApplicationException(
                $"O provisório {document.Code} já tem {binding.ToString("N2", PtBr)} composto/baixado: " +
                $"o washout o deixaria em {newAmount.ToString("N2", PtBr)}. Desfaça a composição ou estorne as baixas antes de aprovar.");

        if (remainingVolume <= 0)
        {
            FinancialDocumentsCancelService.Cancel(document, SalesWashedOutCancellationReason, userName);
            return;
        }

        if (newAmount == document.NetAmount) return;

        changeLog.Register(
            document.Key,
            FinancialDocumentChangeLogFields.NetAmount,
            document.NetAmount.ToString("N2", PtBr),
            newAmount.ToString("N2", PtBr),
            userName);

        document.NetAmount = newAmount;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
    }
}
