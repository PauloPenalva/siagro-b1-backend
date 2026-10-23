using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.EfdContributions;

/// <summary>
/// Quais linhas entram na EFD Contribuições. O Guia Prático pede os documentos que são receita
/// ou que geram crédito; remessa, retorno, transferência e entrada sem crédito ficam de fora,
/// seja qual for o CST cadastrado (uma remessa com CST 08 viraria "receita sem incidência").
/// </summary>
public static class EfdContributionsScope
{
    /// <summary>Saídas que, apesar da faixa x9xx, são receita: entrega futura, transporte e serviço.</summary>
    private static readonly HashSet<int> RevenueExceptions = [922, 932, 933];

    public static bool IsDeclared(FiscalDocumentLineDto line, bool nonCumulative)
    {
        var cfop = line.Cfop?.Trim() ?? "";
        // Sem CFOP a linha continua no arquivo para o validador bloquear a geração.
        if (cfop.Length != 4 || !int.TryParse(cfop[1..], out var operation))
            return true;

        if (line.Direction == "S")
            return !IsNonRevenueOutput(operation);

        // Entrada: devolução de venda abate a receita; o resto só entra se gera crédito (filial não
        // cumulativa). O crédito presumido (60-67) entra para o validador bloquear, em vez de sumir.
        if (operation is >= 201 and <= 209)
            return true;
        return nonCumulative && (IsCredit(line.CstPis) || IsCredit(line.CstCofins));
    }

    private static bool IsCredit(string? cst) =>
        EfdContributionsValidator.CreditCsts.Contains(cst ?? "")
        || EfdContributionsValidator.UnsupportedCreditCsts.Contains(cst ?? "");

    /// <summary>Transferência, devolução de compra, venda do ativo imobilizado e remessas/retornos.</summary>
    private static bool IsNonRevenueOutput(int operation) => operation switch
    {
        >= 151 and <= 159 => true,
        >= 201 and <= 210 => true,
        >= 551 and <= 557 => true,
        >= 901 and <= 949 => !RevenueExceptions.Contains(operation),
        _ => false,
    };
}
