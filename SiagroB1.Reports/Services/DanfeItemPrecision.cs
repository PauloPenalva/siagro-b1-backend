using System.Globalization;
using NFe.Classes.Informacoes.Detalhe;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Casas de quantidade e valor unitário do DANFE e se ele imprime as duas unidades, tiradas dos
/// <c>det/prod</c> do XML autorizado (spec 2026-10-14 §1). O layout da Zeus só aceita uma configuração
/// por nota: vale a maior precisão entre todos os itens, nos grupos comercial e tributável.
/// </summary>
public sealed record DanfeItemPrecision(int QuantityDecimals, int UnitPriceDecimals, bool BothUnits)
{
    private const int MinDecimals = 2;
    private const int MaxQuantityDecimals = 4;   // TDec_1104v
    private const int MaxUnitPriceDecimals = 10; // TDec_1110v

    public static DanfeItemPrecision From(IEnumerable<prod> products)
    {
        var list = products.ToList();

        return new DanfeItemPrecision(
            Decimals(list.SelectMany(p => new[] { p.qCom, p.qTrib }), MaxQuantityDecimals),
            Decimals(list.SelectMany(p => new[] { p.vUnCom, p.vUnTrib }), MaxUnitPriceDecimals),
            list.Any(p => p.uTrib != p.uCom || p.qTrib != p.qCom));
    }

    private static int Decimals(IEnumerable<decimal> values, int max) =>
        Math.Clamp(values.Select(SignificantDecimals).DefaultIfEmpty(0).Max(), MinDecimals, max);

    /// <summary>Casas sem os zeros à direita: o parse do XML preserva a escala do texto ("2.0000000000").</summary>
    private static int SignificantDecimals(decimal value)
    {
        // ⚠️ Não normalize por divisão: ela perde casas em valores com muitas casas.
        var text = value.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text[(dot + 1)..].TrimEnd('0').Length;
    }
}
