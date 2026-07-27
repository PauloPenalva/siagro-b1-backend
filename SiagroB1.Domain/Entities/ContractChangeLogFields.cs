using System.Globalization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos gravados na coluna <c>Field</c> dos logs de alterações: contrato de compra, contrato de
/// venda e documento de saída. São contrato com a tela: o formatter do frontend traduz cada código
/// para o rótulo em pt-BR. Não renomeie sem migrar as linhas já gravadas.
///
/// O nome da classe é histórico — nasceu com o log do contrato. Os códigos são compartilhados de
/// propósito: o documento de saída grava o mesmo <see cref="Comment"/>, e a tela o traduz com o
/// mesmo formatter.
/// </summary>
public static class ContractChangeLogFields
{
    public const string DeliveryLocation = "DeliveryLocation";
    public const string Attachment = "Attachment";
    public const string PriceFixation = "PriceFixation";

    /// <summary>
    /// Comentário do contrato ou do documento de saída (coleção <c>CommentEntries</c>). Singular de
    /// propósito: o código legado <c>Comments</c> é a OBSERVAÇÃO do cabeçalho, que já tem linhas
    /// gravadas no banco.
    /// </summary>
    public const string Comment = "Comment";

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
