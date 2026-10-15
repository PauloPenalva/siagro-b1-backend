namespace SiagroB1.Fiscal.Nfe;

/// <summary>Unidade tributável de exportação do produto: <c>uTrib</c> e quantas unidades da linha (KG) cabem nela.</summary>
public sealed record NfeTaxableUnitSetting(string UnitOfMeasure, decimal Factor);

/// <summary>Conversão da linha (KG) para o grupo tributável (spec 2026-10-14 §4).</summary>
public static class NfeTaxableUnit
{
    /// <summary>
    /// <c>qTrib</c> = quantidade ÷ fator com 4 casas (<c>TDec_1104v</c>); <c>vUnTrib</c> = <c>vProd</c> ÷ <c>qTrib</c> com
    /// 10 casas (<c>TDec_1110v</c>). <c>qTrib</c> zero (nota de complemento, quantidade ínfima) dá <c>vUnTrib</c> zero.
    /// </summary>
    public static (decimal Quantity, decimal UnitPrice) Convert(decimal quantity, decimal total, decimal factor)
    {
        var taxableQuantity = Math.Round(quantity / factor, 4, MidpointRounding.AwayFromZero);
        var unitPrice = taxableQuantity == 0m ? 0m : Math.Round(total / taxableQuantity, 10, MidpointRounding.AwayFromZero);

        return (taxableQuantity, unitPrice);
    }
}
