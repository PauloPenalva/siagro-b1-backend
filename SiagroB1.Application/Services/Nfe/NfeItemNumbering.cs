using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// O <c>det/@nItem</c> de cada linha (spec §8.2). Numerado na emissão e GRAVADO na linha: a NF-e de
/// devolução referencia o item da venda por esse número (<c>DFeReferenciado</c>), então ele tem de ser
/// o mesmo do XML autorizado. Renumera 1..n a cada tentativa — antes da autorização linhas ainda
/// entram e saem, e a sequência não pode ter buracos — mantendo a ordem anterior.
/// </summary>
/// <remarks>
/// A ordem é a do número já gravado e, para as linhas novas, a da <c>Key</c> comparada em MEMÓRIA
/// (<see cref="Guid"/> do .NET): o SQL Server ordena <c>uniqueidentifier</c> de outro jeito.
/// </remarks>
public static class NfeItemNumbering
{
    public static IReadOnlyList<TLine> Ordered<TLine>(IEnumerable<TLine> items) where TLine : INfeTaxedLine =>
        items.OrderBy(i => i.NfeItemNumber ?? int.MaxValue).ThenBy(i => i.Key).ToList();

    public static void Renumber<TLine>(IEnumerable<TLine> items) where TLine : INfeTaxedLine
    {
        var ordered = Ordered(items);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].NfeItemNumber = i + 1;
    }

    /// <summary>
    /// Número da linha de origem na NF-e da operação original (venda ou compra). Origem autorizada antes desta numeração existir: com
    /// um item só, ele é o 1; com vários, não há como saber — nulo.
    /// </summary>
    public static int? OriginNumber(INfeTaxedLine originItem, int originItemCount) =>
        originItem.NfeItemNumber ?? (originItemCount == 1 ? 1 : null);
}
