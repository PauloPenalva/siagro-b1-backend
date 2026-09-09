using System.Globalization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos gravados na coluna <c>Field</c> de <see cref="ShipmentLoadChangeLog"/>. São contrato
/// com a tela: o formatter do frontend traduz cada código para o rótulo em pt-BR. Não renomeie
/// sem migrar as linhas já gravadas.
/// </summary>
public static class ShipmentLoadChangeLogFields
{
    /// <summary>
    /// Comentário da carga (coleção <c>CommentEntries</c>). Mesma string usada pelos contratos e
    /// pelos documentos — é o que permite reusar <c>ContractCommentRules</c> sem tocar em nada.
    /// Singular de propósito: <see cref="Comments"/> é a OBSERVAÇÃO do cabeçalho.
    /// </summary>
    public const string Comment = "Comment";

    public const string LoadDate = "LoadDate";
    public const string TruckCode = "TruckCode";
    public const string TruckDriver = "TruckDriver";
    public const string Carrier = "Carrier";
    public const string CardCode = "CardCode";
    public const string Warehouse = "Warehouse";
    public const string Item = "Item";
    public const string UnitOfMeasure = "UnitOfMeasure";
    public const string Branch = "Branch";
    public const string HasExcess = "HasExcess";
    public const string FreightPrice = "FreightPrice";

    /// <summary>Observação escalar do cabeçalho. Plural, distinto de <see cref="Comment"/>.</summary>
    public const string Comments = "Comments";

    public const string Status = "Status";
    public const string CancellationReason = "CancellationReason";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Data como ela aparece no log. Formatar no servidor mantém "de" e "para" comparáveis
    /// mesmo que a máscara da tela mude depois — e a tela não teria como saber que aquela linha
    /// é uma data: <c>OldValue</c>/<c>NewValue</c> são texto livre.
    /// </summary>
    public static string DescribeDate(DateTime value) => value.ToString("dd/MM/yyyy", PtBr);

    public static string DescribeBoolean(bool value) => value ? "Sim" : "Não";

    /// <summary>
    /// Nulo devolve nulo: o log já usa nulo para "não havia valor", e "frete não informado" é
    /// diferente de "frete zero".
    /// </summary>
    public static string? DescribeFreightPrice(decimal? value) =>
        value?.ToString("N2", PtBr);

    public static string DescribeStatus(ShipmentLoadStatus status) => status switch
    {
        ShipmentLoadStatus.Planned => "Planejada",
        ShipmentLoadStatus.Open => "Carregada",
        ShipmentLoadStatus.PartiallyInvoiced => "Faturada Parcial",
        ShipmentLoadStatus.Invoiced => "Faturada",
        ShipmentLoadStatus.Cancelled => "Cancelada",
        ShipmentLoadStatus.Returned => "Devolvida",
        _ => status.ToString(),
    };
}
