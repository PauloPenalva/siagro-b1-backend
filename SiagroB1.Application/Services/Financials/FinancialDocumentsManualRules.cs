using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Título lançado à mão nas telas de Contas a Pagar/Receber: origem Manual e natureza Firme. O adiantamento também nasce
/// com origem Manual, mas tem tela e regras próprias — por isso a natureza entra na conta.
/// </summary>
public static class FinancialDocumentsManualRules
{
    public static bool IsManualTitle(FinancialDocument document) =>
        document.OriginType == FinancialDocumentOrigin.Manual &&
        document.Nature == FinancialDocumentNature.Firm;

    public static void Validate(string? cardCode, string? branchCode, DateTime documentDate, DateTime dueDate, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ApplicationException("Informe o parceiro de negócio.");

        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ApplicationException("Informe a filial.");

        if (amount <= 0m)
            throw new ApplicationException("O valor do título deve ser maior que zero.");

        if (dueDate == default)
            throw new ApplicationException("Informe o vencimento.");

        if (dueDate.Date < documentDate.Date)
            throw new ApplicationException("O vencimento não pode ser anterior à data de emissão.");
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
