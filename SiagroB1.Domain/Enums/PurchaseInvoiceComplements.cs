namespace SiagroB1.Domain.Enums;

/// <summary>Os tipos de documento de entrada que são "complemento" (spec 2026-10-17 D1): o de valor e o de ICMS.</summary>
public static class PurchaseInvoiceComplements
{
    public static bool IsComplement(PurchaseInvoiceType type) =>
        type is PurchaseInvoiceType.ValueComplement or PurchaseInvoiceType.IcmsComplement;

    /// <summary>O nome do tipo como aparece no meio das mensagens: "complemento de valor" / "complemento de ICMS".</summary>
    public static string Label(PurchaseInvoiceType type) => type switch
    {
        PurchaseInvoiceType.ValueComplement => "complemento de valor",
        PurchaseInvoiceType.IcmsComplement => "complemento de ICMS",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "O tipo não é complemento."),
    };
}
