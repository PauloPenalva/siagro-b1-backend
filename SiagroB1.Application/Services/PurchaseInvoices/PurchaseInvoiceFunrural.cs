using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Escritor único da fotografia do Funrural na linha do Documento de Entrada (spec 2026-10-09 §3–§6). A linha de saída
/// não tem esses campos, por isso eles ficam fora de <c>INfeTaxedLine</c>/<c>TaxSnapshot</c>.
/// </summary>
public static class PurchaseInvoiceFunrural
{
    /// <summary>Calcula sobre o total geral da linha (a mesma base do ICMS) e grava.</summary>
    public static void Apply(PurchaseInvoiceItem item, decimal? inssRate, decimal? ratRate, decimal? senarRate) =>
        Write(item, FunruralCalculator.Calculate(item.GrandTotal, inssRate, ratRate, senarRate));

    /// <summary>Linha sem Funrural: descarta o que tenha vindo no corpo da requisição.</summary>
    public static void Clear(PurchaseInvoiceItem item) => Write(item, FunruralResult.None);

    /// <summary>Trecho do infCpl; na devolução de compra, prefixado como estorno.</summary>
    public static string? Text(PurchaseInvoice invoice) =>
        FunruralText.Build(
            invoice.Items.Select(i => new FunruralTextLine(
                i.FunruralInssRate, i.FunruralRatRate, i.FunruralSenarRate,
                i.FunruralInssValue, i.FunruralRatValue, i.FunruralSenarValue)),
            invoice.IsNfeReturn);

    private static void Write(PurchaseInvoiceItem item, FunruralResult r)
    {
        item.FunruralBase = r.Base;
        item.FunruralInssRate = r.InssRate;
        item.FunruralRatRate = r.RatRate;
        item.FunruralSenarRate = r.SenarRate;
        item.FunruralInssValue = r.InssValue;
        item.FunruralRatValue = r.RatValue;
        item.FunruralSenarValue = r.SenarValue;
    }
}
