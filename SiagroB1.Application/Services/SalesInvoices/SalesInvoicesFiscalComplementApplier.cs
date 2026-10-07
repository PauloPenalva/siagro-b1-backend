using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Aplica o complemento fiscal do contrato ao documento de saída (spec 2026-10-07 §4.2). Só na filial que emite NF-e
/// pelo Siagro, em documento Normal (não devolução), e só nas linhas com contrato. Natureza e condição do complemento
/// SOBRESCREVEM o corpo: quem fatura não decide o fiscal (D6).
/// </summary>
public class SalesInvoicesFiscalComplementApplier(IUnitOfWork db, TaxCalculationGate gate)
{
    private const string Separator = " | ";

    private const string DifferentConditions =
        "Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal.";

    public async Task ApplyToDocumentAsync(SalesInvoice invoice)
    {
        var complements = await ResolveAsync(invoice, invoice.Items);
        if (complements is null) return;

        foreach (var item in invoice.Items)
            if (item.SalesContractKey is { } key) ApplyLine(item, complements[key]);

        var conditions = complements.Values.Select(c => c.PaymentConditionCode).Distinct().ToList();
        if (conditions.Count > 1)
            throw new DefaultException(DifferentConditions);
        invoice.PaymentConditionCode = conditions[0];

        // Texto do contrato ANTES do texto do operador; reaplicar não repete.
        foreach (var text in complements.Values.Select(c => c.AdditionalInfo).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
            if (invoice.TaxPayerComments?.Contains(text!) != true)
                invoice.TaxPayerComments = string.IsNullOrWhiteSpace(invoice.TaxPayerComments) ? text : text + Separator + invoice.TaxPayerComments;
    }

    public async Task ApplyToLineAsync(SalesInvoice invoice, SalesInvoiceItem item)
    {
        var complements = await ResolveAsync(invoice, [item]);
        if (complements is null || item.SalesContractKey is not { } key) return;

        var complement = complements[key];
        ApplyLine(item, complement);

        if (invoice.PaymentConditionCode is null)
            invoice.PaymentConditionCode = complement.PaymentConditionCode;
        else if (invoice.PaymentConditionCode != complement.PaymentConditionCode)
            throw new DefaultException(DifferentConditions);
    }

    /// <summary>Null = regra não se aplica. Recusa contrato sem complemento completo, antes de qualquer gravação.</summary>
    private async Task<Dictionary<Guid, SalesContractFiscalComplement>?> ResolveAsync(
        SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Normal || invoice.IsNfeReturn) return null;

        var keys = items.Where(i => i.SalesContractKey.HasValue).Select(i => i.SalesContractKey!.Value).Distinct().ToList();
        if (keys.Count == 0 || !await gate.IsActiveAsync(invoice.BranchCode)) return null;

        var complements = await db.Context.SalesContractFiscalComplements.AsNoTracking()
            .Where(c => keys.Contains(c.SalesContractKey)).ToDictionaryAsync(c => c.SalesContractKey);

        foreach (var key in keys)
        {
            if (complements.TryGetValue(key, out var c) && c.UsageCode.HasValue && c.PaymentConditionCode.HasValue) continue;

            var code = await db.Context.SalesContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Code).FirstOrDefaultAsync();
            throw new DefaultException(
                $"O contrato {code} não tem complemento fiscal com natureza de operação e condição de pagamento. Peça ao fiscal para completá-lo.");
        }

        return complements;
    }

    private static void ApplyLine(SalesInvoiceItem item, SalesContractFiscalComplement complement)
    {
        item.UsageCode = complement.UsageCode;
        item.CustomerOrderNumber = complement.CustomerOrderNumber;
        item.CustomerOrderItem = complement.CustomerOrderItem;
    }
}
