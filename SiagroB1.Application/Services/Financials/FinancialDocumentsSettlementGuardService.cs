using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Regra ÚNICA de "este documento pode ser baixado?". Estático e sem estado de propósito: a
/// baixa, o estorno e (na Fase 4) o pagamento em lote têm de recusar exatamente pelos mesmos
/// motivos.
/// </summary>
public static class FinancialDocumentsSettlementGuardService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static void EnsureCanSettle(
        FinancialDocument document, FinancialAccount? account, decimal amount)
    {
        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        // Regra 15 (spec 2026-10-13, títulos firmes): firme e devolução de contrato (compra ou venda) saem só por composição no
        // provisório, dação ou encontro de contas — nunca por baixa em dinheiro.
        if (document.IsContractFirmDocument)
            throw new ApplicationException(
                "Título firme não recebe baixa direta: componha no provisório ou baixe por dação.");

        if (account is null)
            throw new ApplicationException("Informe a conta financeira da baixa.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != document.Currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o documento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        if (amount <= 0m)
            throw new ApplicationException("O valor da baixa deve ser maior que zero.");

        // Regra 15: o provisório paga só o que está coberto por firme composto (trava rígida).
        if (document.Nature == FinancialDocumentNature.Provisional && amount > document.AvailableToSettleAmount)
            throw new ApplicationException(
                $"Valor acima do disponível para baixa ({document.AvailableToSettleAmount.ToString("N2", PtBr)}): " +
                "componha mais títulos firmes antes.");

        if (amount > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da baixa ({amount:N2}) excede o saldo em aberto do documento " +
                $"({document.OpenAmount:N2}).");
    }
}
