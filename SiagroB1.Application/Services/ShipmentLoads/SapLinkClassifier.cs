using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>NF do SAP a vincular: quantidade e total das linhas.</summary>
public sealed record SapLinkInvoice(int DocEntry, decimal Quantity, decimal LineTotal);

/// <summary>Parte da quantidade efetiva (QE) atribuída a uma NF.</summary>
public sealed record SapLinkShare(int DocEntry, decimal EffectiveQuantity, decimal InvoiceQuantity,
    decimal? EffectiveValue, decimal InvoiceValue)
{
    public decimal QuantityDifference => EffectiveQuantity - InvoiceQuantity;
    public decimal? ValueDifference => EffectiveValue - InvoiceValue;
}

/// <summary>Resultado da classificação do vínculo de NFs do SAP com a carga.</summary>
public sealed record SapLinkAssessment(SapLinkType Type, decimal EffectiveQuantity, decimal InvoiceQuantity,
    decimal? EffectiveValue, decimal InvoiceValue, IReadOnlyList<SapLinkShare> Shares)
{
    /// <summary>QE − QNF.</summary>
    public decimal QuantityDifference => EffectiveQuantity - InvoiceQuantity;

    /// <summary>VE − VNF; nulo sem preço de referência.</summary>
    public decimal? ValueDifference => EffectiveValue - InvoiceValue;

    /// <summary>Texto fixo do tipo; nulo no tipo normal.</summary>
    public string? Message => Type switch
    {
        SapLinkType.ValueDivergent =>
            "Valor(es) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?",
        SapLinkType.QuantityAndValueDivergent =>
            "Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?",
        _ => null,
    };
}

/// <summary>
/// Classificador puro do vínculo de NF do SAP: compara quantidade efetiva (QE) com a da NF (QNF)
/// e valor efetivo (VE = QE × preço de referência) com o da NF (VNF = Σ total das linhas).
/// </summary>
public static class SapLinkClassifier
{
    public const decimal QuantityTolerance = 0.001m;
    public const decimal ValueTolerance = 0.01m;

    public static SapLinkAssessment Assess(decimal effectiveQuantity, decimal? referencePrice,
        IReadOnlyList<SapLinkInvoice> invoices)
    {
        if (invoices is null || invoices.Count == 0)
            throw new ArgumentException("Informe ao menos uma NF.", nameof(invoices));

        var invoiceQuantity = invoices.Sum(i => i.Quantity);
        if (invoiceQuantity <= 0m)
            throw new ArgumentException("A quantidade total das NFs deve ser maior que zero.", nameof(invoices));

        var invoiceValue = Round(invoices.Sum(i => i.LineTotal), 2);
        decimal? effectiveValue = referencePrice is null ? null : Round(effectiveQuantity * referencePrice.Value, 2);

        // Rateio da QE pela quantidade de cada NF; a última leva o resto para a soma fechar exata.
        var shares = new List<SapLinkShare>(invoices.Count);
        var allocated = 0m;
        for (var i = 0; i < invoices.Count; i++)
        {
            var invoice = invoices[i];
            var shareQuantity = i == invoices.Count - 1
                ? effectiveQuantity - allocated
                : Round(effectiveQuantity * invoice.Quantity / invoiceQuantity, 3);
            allocated += shareQuantity;

            decimal? shareValue = referencePrice is null ? null : Round(shareQuantity * referencePrice.Value, 2);
            shares.Add(new SapLinkShare(invoice.DocEntry, shareQuantity, invoice.Quantity, shareValue, invoice.LineTotal));
        }

        SapLinkType type;
        if (Math.Abs(effectiveQuantity - invoiceQuantity) > QuantityTolerance)
            type = SapLinkType.QuantityAndValueDivergent;
        else if (effectiveValue is null)
            type = SapLinkType.Normal;
        else if (Math.Abs(effectiveValue.Value - invoiceValue) > ValueTolerance)
            type = SapLinkType.ValueDivergent;
        else
            type = SapLinkType.Normal;

        return new SapLinkAssessment(type, effectiveQuantity, invoiceQuantity, effectiveValue, invoiceValue, shares);
    }

    private static decimal Round(decimal value, int decimals) =>
        decimal.Round(value, decimals, MidpointRounding.ToEven);
}
