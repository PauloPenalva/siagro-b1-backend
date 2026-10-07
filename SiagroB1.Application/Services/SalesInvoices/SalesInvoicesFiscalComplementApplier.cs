using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
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
        var resolved = await ResolveAsync(invoice, ContractKeys(invoice.Items));
        if (resolved is null) return;

        foreach (var item in invoice.Items)
            if (item.SalesContractKey is { } key) ApplyLine(item, resolved[key]);

        ApplyHeader(invoice, resolved.Ordered);
    }

    public async Task ApplyToLineAsync(SalesInvoice invoice, SalesInvoiceItem item)
    {
        var resolved = await ResolveAsync(invoice, ContractKeys([item]));
        if (resolved is null || item.SalesContractKey is not { } key) return;

        var complement = resolved[key];
        ApplyLine(item, complement);

        if (invoice.PaymentConditionCode is null)
            invoice.PaymentConditionCode = complement.PaymentConditionCode;
        else if (invoice.PaymentConditionCode != complement.PaymentConditionCode)
            throw new DefaultException(DifferentConditions);

        // Regra reaplicada na linha também leva o texto do contrato, sem repetir (spec §4.2 item 3).
        PrependText(invoice, [complement]);
    }

    /// <summary>
    /// Edição do cabeçalho: o PATCH reenvia a entidade inteira, então condição e texto do contrato voltam em silêncio
    /// (sem recusa) a partir das linhas GRAVADAS. Só em documento Pendente e com NF-e não emitida — depois disso o que
    /// foi para a nota não muda.
    /// </summary>
    public async Task ApplyToHeaderAsync(SalesInvoice invoice)
    {
        if (!Applies(invoice) || invoice.InvoiceStatus is not (null or InvoiceStatus.Pending)
            || NfeLockRules.IsFrozen(invoice.NfeStatus))
            return;

        var keys = await db.Context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.SalesInvoiceKey == invoice.Key && i.SalesContractKey != null)
            .Select(i => i.SalesContractKey!.Value)
            .ToListAsync();

        var resolved = await ResolveAsync(invoice, keys.Distinct().ToList());
        if (resolved is null) return;

        ApplyHeader(invoice, resolved.Ordered);
    }

    private static bool Applies(SalesInvoice invoice) =>
        invoice.InvoiceType == SalesInvoiceType.Normal && !invoice.IsNfeReturn;

    private static List<Guid> ContractKeys(IEnumerable<SalesInvoiceItem> items) =>
        items.Where(i => i.SalesContractKey.HasValue).Select(i => i.SalesContractKey!.Value).Distinct().ToList();

    private static void ApplyHeader(SalesInvoice invoice, IReadOnlyList<SalesContractFiscalComplement> complements)
    {
        var conditions = complements.Select(c => c.PaymentConditionCode).Distinct().ToList();
        if (conditions.Count > 1)
            throw new DefaultException(DifferentConditions);
        invoice.PaymentConditionCode = conditions[0];

        PrependText(invoice, complements);
    }

    /// <summary>
    /// Textos dos contratos na ordem em que aparecem nas linhas, depois o texto do operador (A | B | operador).
    /// O texto que já está no campo não se repete — é isso que deixa a regra ser reaplicada.
    /// </summary>
    private static void PrependText(SalesInvoice invoice, IEnumerable<SalesContractFiscalComplement> complements)
    {
        var current = invoice.TaxPayerComments;
        var missing = complements.Select(c => c.AdditionalInfo)
            .Where(t => !string.IsNullOrWhiteSpace(t) && current?.Contains(t!) != true)
            .Distinct()
            .ToList();

        if (missing.Count == 0) return;

        var contractText = string.Join(Separator, missing);
        invoice.TaxPayerComments = string.IsNullOrWhiteSpace(current) ? contractText : contractText + Separator + current;
    }

    private sealed class Resolved(Dictionary<Guid, SalesContractFiscalComplement> byKey, List<Guid> keys)
    {
        public SalesContractFiscalComplement this[Guid key] => byKey[key];
        public IReadOnlyList<SalesContractFiscalComplement> Ordered { get; } = keys.Select(k => byKey[k]).ToList();
    }

    /// <summary>Null = regra não se aplica. Recusa contrato sem complemento completo, antes de qualquer gravação.</summary>
    private async Task<Resolved?> ResolveAsync(SalesInvoice invoice, List<Guid> keys)
    {
        if (!Applies(invoice)) return null;
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

        return new Resolved(complements, keys);
    }

    private static void ApplyLine(SalesInvoiceItem item, SalesContractFiscalComplement complement)
    {
        item.UsageCode = complement.UsageCode;
        item.CustomerOrderNumber = complement.CustomerOrderNumber;
        item.CustomerOrderItem = complement.CustomerOrderItem;
    }
}
