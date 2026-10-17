using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Escritor único das retenções de serviço no cabeçalho do Documento de Entrada (spec 2026-10-17 §2). Fora de "outra
/// compra" zera tudo; com <see cref="PurchaseInvoice.WithholdingsManual"/> preserva o que o usuário digitou e só valida;
/// sem ele, recalcula com as alíquotas da natureza.
/// </summary>
public static class PurchaseInvoiceWithholdings
{
    public static async Task ApplyAsync(PurchaseInvoice invoice, IUsage usages, bool otherPurchase)
    {
        if (!otherPurchase)
        {
            Write(invoice, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
            invoice.WithholdingsManual = false;
            return;
        }

        if (!invoice.WithholdingsManual)
        {
            var rates = await ResolveRatesAsync(invoice, usages);
            var baseValue = invoice.GrandTotal;
            decimal Value(decimal rate) => decimal.Round(baseValue * rate / 100m, 2, MidpointRounding.ToEven);

            Write(invoice, baseValue, Value(rates[0]), Value(rates[1]), Value(rates[2]),
                Value(rates[3]), Value(rates[4]), Value(rates[5]));
        }

        Validate(invoice);
    }

    private static async Task<decimal[]> ResolveRatesAsync(PurchaseInvoice invoice, IUsage usages)
    {
        var codes = invoice.Items.Where(i => i.UsageCode is not null).Select(i => i.UsageCode!.Value).Distinct().ToList();
        decimal[]? first = null;

        foreach (var code in codes)
        {
            var usage = await usages.GetByIdAsync(code);
            if (usage is null)
                continue;

            var rates = RatesOf(usage);
            if (first is null)
                first = rates;
            else if (!first.SequenceEqual(rates))
                throw new DefaultException("As linhas têm naturezas com alíquotas de retenção diferentes.");
        }

        return first ?? new decimal[6];
    }

    private static decimal[] RatesOf(UsageModel u) =>
    [
        u.WithholdingIssRate ?? 0m, u.WithholdingIrrfRate ?? 0m, u.WithholdingInssRate ?? 0m,
        u.WithholdingPisRate ?? 0m, u.WithholdingCofinsRate ?? 0m, u.WithholdingCsllRate ?? 0m,
    ];

    private static void Validate(PurchaseInvoice invoice)
    {
        if (invoice.WithholdingBase < 0m || invoice.WithheldIss < 0m || invoice.WithheldIrrf < 0m || invoice.WithheldInss < 0m
            || invoice.WithheldPis < 0m || invoice.WithheldCofins < 0m || invoice.WithheldCsll < 0m)
            throw new DefaultException("O valor retido não pode ser negativo.");

        if (invoice.TotalWithheld > invoice.GrandTotal)
            throw new DefaultException("O total retido não pode ser maior que o total do documento.");
    }

    private static void Write(PurchaseInvoice i, decimal baseValue, decimal iss, decimal irrf, decimal inss, decimal pis,
        decimal cofins, decimal csll)
    {
        i.WithholdingBase = baseValue;
        i.WithheldIss = iss;
        i.WithheldIrrf = irrf;
        i.WithheldInss = inss;
        i.WithheldPis = pis;
        i.WithheldCofins = cofins;
        i.WithheldCsll = csll;
    }
}
