using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Ajusta NO LUGAR o provisório a pagar de uma fixação cujo volume mudou por washout.
///
/// No lugar, e não cancelar-e-gerar: provisório não aceita baixa, então mudar o valor não afeta
/// nada realizado; e cancelar-e-gerar exigiria gravar o cancelamento antes do insert (índice único
/// filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin) e buscar número por Dapper no meio da
/// transação.
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
            FinancialDocumentsCancelService.Cancel(document, WashedOutCancellationReason, userName);
            return;
        }

        var newAmount = decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);
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
    }
}
