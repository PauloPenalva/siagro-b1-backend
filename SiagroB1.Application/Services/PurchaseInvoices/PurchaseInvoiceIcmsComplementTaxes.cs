using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributos da linha do "Complemento de ICMS" da entrada (spec 2026-10-17 D4): a regra comum de
/// <see cref="IcmsComplementTaxes"/> e, só na entrada, sem Funrural.
/// </summary>
internal static class PurchaseInvoiceIcmsComplementTaxes
{
    /// <inheritdoc cref="IcmsComplementTaxes.CstFor"/>
    public static string CstFor(string? originalCst) => IcmsComplementTaxes.CstFor(originalCst);

    /// <summary>Grava o ICMS informado, zera os demais tributos (<see cref="IcmsComplementTaxes.Apply"/>) e o Funrural.</summary>
    public static void Apply(PurchaseInvoiceItem line, string cst, decimal typedBase, decimal typedValue)
    {
        IcmsComplementTaxes.Apply(line, cst, typedBase, typedValue);
        PurchaseInvoiceFunrural.Clear(line);
    }
}
