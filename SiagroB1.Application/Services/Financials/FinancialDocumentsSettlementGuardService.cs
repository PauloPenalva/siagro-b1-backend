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
    public static void EnsureCanSettle(
        FinancialDocument document, FinancialAccount? account, decimal amount)
    {
        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        if (document.IsBlockedForSettlement)
            throw new ApplicationException(
                $"O documento {document.Code} é provisório e não pode ser baixado. " +
                "Ele será liberado quando o documento fiscal for confirmado.");

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

        if (amount > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da baixa ({amount:N2}) excede o saldo em aberto do documento " +
                $"({document.OpenAmount:N2}).");
    }
}
