using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Classifica a NF-e do fornecedor: normal, complemento de valor ou complemento de ICMS (spec 2026-10-17 D7).
/// Complemento de ICMS é o finNFe 2 em que TODO item tem vProd zerado e ICMS destacado; qualquer item com valor
/// (ou sem ICMS) faz o documento ser complemento de valor, que é o comportamento anterior.
/// </summary>
public static class SupplierNfeComplementKind
{
    public static PurchaseInvoiceType Classify(SupplierNfe nfe)
    {
        if (!nfe.IsComplement)
            return PurchaseInvoiceType.Normal;

        return nfe.Items.Count > 0 && nfe.Items.All(i => i.ProductValue == 0m && i.IcmsValue > 0m)
            ? PurchaseInvoiceType.IcmsComplement
            : PurchaseInvoiceType.ValueComplement;
    }
}
