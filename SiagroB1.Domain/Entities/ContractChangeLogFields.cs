using System.Globalization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos gravados na coluna <c>Field</c> do log de alterações do contrato (compra e venda).
/// São contrato com a tela: o formatter do frontend traduz cada código para o rótulo em pt-BR.
/// Não renomeie sem migrar as linhas já gravadas.
/// </summary>
public static class ContractChangeLogFields
{
    public const string DeliveryLocation = "DeliveryLocation";
    public const string Attachment = "Attachment";
    public const string PriceFixation = "PriceFixation";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Como a fixação aparece no log: volume, preço e situação juntos.
    ///
    /// O volume e o preço vão em TODAS as linhas — inclusive nas de mudança de status — porque
    /// um contrato tem várias fixações, e "Em aprovação → Confirmada" sozinho não diria qual
    /// delas mudou. O texto também sobrevive à exclusão da fixação.
    /// </summary>
    public static string DescribePriceFixation(
        decimal volume, decimal price, PriceFixationStatus status, string? unitOfMeasureCode = null)
    {
        var unit = string.IsNullOrWhiteSpace(unitOfMeasureCode) ? "" : $" {unitOfMeasureCode}";

        return string.Format(
            PtBr,
            "{0:N3}{1} @ {2:N2} — {3}",
            volume, unit, price, DescribeStatus(status));
    }

    /// <summary>
    /// Situação da fixação em pt-BR. Fica aqui, e não num formatter do frontend, porque é
    /// gravada dentro de um texto composto que a tela não teria como desmontar.
    /// </summary>
    private static string DescribeStatus(PriceFixationStatus status) => status switch
    {
        PriceFixationStatus.InApproval => "Em aprovação",
        PriceFixationStatus.Confirmed => "Confirmada",
        PriceFixationStatus.Canceled => "Cancelada",
        PriceFixationStatus.Rejected => "Rejeitada",
        _ => status.ToString(),
    };
}
