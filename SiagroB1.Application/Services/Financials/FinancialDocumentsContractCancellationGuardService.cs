using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// "Este contrato pode ser cancelado, do ponto de vista do financeiro?"
///
/// A regra é financeira, então mora no financeiro; os dois serviços de cancelamento de contrato
/// a chamam ao lado do guard de movimento físico que já existe. Fecha a assimetria da Fase 1,
/// em que movimento de grão bloqueava o cancelamento e dinheiro pago não bloqueava nada.
///
/// NÃO vale para ENCERRAMENTO de contrato: encerrar é outro ato — o contrato foi cumprido, e
/// ali o adiantamento é matéria da amortização (Fase 2), não coisa a bloquear.
/// </summary>
public class FinancialDocumentsContractCancellationGuardService(IUnitOfWork db)
{
    public async Task EnsureCanCancelAsync(Guid? purchaseContractKey, Guid? salesContractKey)
    {
        // Sem chave nenhuma o WHERE abaixo nunca bate em nada e o guard passa em silêncio —
        // falha aberta. Um chamador futuro que esquecer de passar a chave do contrato precisa de
        // um erro alto, não de um cancelamento liberado por engano.
        if (purchaseContractKey is null && salesContractKey is null)
            throw new ArgumentException(
                "Informe a chave do contrato de compra ou de venda para verificar o cancelamento.");

        var blocking = await db.Context.FinancialDocuments
            .AsNoTracking()
            .Where(x => x.Nature == FinancialDocumentNature.Advance &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.SettledAmount != 0m &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .Select(x => new { x.Code, x.SettledAmount, x.Currency })
            .ToListAsync();

        if (blocking.Count == 0) return;

        var titles = string.Join(", ",
            blocking.Select(x => $"{x.Code} ({CurrencySymbol(x.Currency)} {x.SettledAmount:N2})"));

        throw new ApplicationException(
            $"O contrato possui adiantamento pago: {titles}. Estorne a baixa, registre a " +
            "devolução do valor adiantado ou vincule o adiantamento a outro contrato antes de cancelar.");
    }

    // FinancialDocument.Currency é por documento (CurrencyType tem Brl e Usd) — a mensagem tem
    // que refletir a moeda do PRÓPRIO documento, nunca um literal fixo.
    private static string CurrencySymbol(CurrencyType currency) => currency switch
    {
        CurrencyType.Usd => "US$",
        _ => "R$",
    };
}
