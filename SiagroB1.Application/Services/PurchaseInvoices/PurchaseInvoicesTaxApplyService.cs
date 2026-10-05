using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributos da linha do Documento de Entrada pela natureza (spec §6.2). Só age com a regra ativa, documento
/// Pendente e EMISSÃO PRÓPRIA — a entrada Normal (natureza de Entrada, CFOP 1xxx/2xxx) e a devolução de compra
/// criada pelo Devolver (natureza de Saída, CFOP 5xxx/6xxx, conferida contra a linha comprada). Documento de
/// terceiro e a devolução do cliente ficam exatamente como chegaram.
/// </summary>
public class PurchaseInvoicesTaxApplyService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService,
    IbsCbsRatesService ibsCbsRatesService)
{
    private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);

    public Task<bool> IsBranchActiveAsync(string? branchCode) => gate.IsActiveAsync(branchCode);

    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.IssuerType == DocumentIssuerType.Own
        && (invoice.InvoiceType == PurchaseInvoiceType.Normal || IsOwnNfeReturn(invoice))
        && await gate.IsActiveAsync(invoice.BranchCode);

    /// <summary>Devolução de compra criada pelo "Devolver": sai com NF-e própria de saída.</summary>
    public static bool IsOwnNfeReturn(PurchaseInvoice invoice) =>
        invoice.InvoiceType == PurchaseInvoiceType.Return && invoice.IsNfeReturn;

    public async Task ApplyAsync(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var lines = items.ToList();
        var ownReturn = IsOwnNfeReturn(invoice);
        var branch = await _lines.LoadBranchAsync(invoice.BranchCode);
        var supplierState = await TaxLineCalculator.LoadPartnerStateAsync(businessPartnerService, invoice.CardCode);
        var inState = string.Equals(branch.StateCode, supplierState, StringComparison.OrdinalIgnoreCase);

        // Devolução: IBS/CBS pelas alíquotas da DATA DA ENTRADA — ela anula o tributo daquela operação.
        var rateDate = ownReturn
            ? await OriginDateAsync(invoice)
            : DateOnly.FromDateTime(invoice.IssueDate ?? DateTime.Today);
        var origins = ownReturn ? await LoadOriginItemsAsync(lines) : new Dictionary<Guid, PurchaseInvoiceItem>();
        var usages = new Dictionary<int, UsageModel>();
        var number = 0;

        foreach (var item in lines)
        {
            number++;

            if (string.IsNullOrWhiteSpace(item.ItemCode))
                throw new DefaultException($"Informe o produto do item {number}.");

            var usage = await ResolveUsageAsync(item, usages, ownReturn);

            // A mercadoria vem do FORNECEDOR para a filial; a devolução repete a operação da compra, para
            // reproduzir a alíquota creditada (12% de BA→SP, e não 7% de SP→BA).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.Total, inState, supplierState, branch.StateCode!,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: !ownReturn));

            TaxLineCalculator.Apply(item, usage, line);

            if (ownReturn)
                NfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name, "compra");
        }
    }

    private async Task<UsageModel> ResolveUsageAsync(
        PurchaseInvoiceItem item, Dictionary<int, UsageModel> cache, bool ownReturn)
    {
        if (item.UsageCode is not { } code)
            throw new DefaultException(ownReturn
                ? $"O item {item.ItemCode} da devolução está sem natureza de devolução."
                : $"O item {item.ItemCode} está sem natureza de operação.");

        if (!cache.TryGetValue(code, out var usage))
        {
            usage = await usageService.GetByIdAsync(code)
                    ?? throw new DefaultException("Natureza de operação não encontrada.");
            cache[code] = usage;
        }

        if (!ownReturn && usage.Direction != UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de saída e não pode ser usada na entrada.");

        if (ownReturn && usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada na devolução de compra.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }

    private async Task<DateOnly> OriginDateAsync(PurchaseInvoice invoice)
    {
        var date = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Key == invoice.PurchaseInvoiceOriginKey)
            .Select(i => i.IssueDate)
            .FirstOrDefaultAsync();

        return DateOnly.FromDateTime(date ?? throw new DefaultException("A devolução está sem a entrada de origem."));
    }

    private async Task<Dictionary<Guid, PurchaseInvoiceItem>> LoadOriginItemsAsync(IEnumerable<PurchaseInvoiceItem> lines)
    {
        var keys = lines.Where(l => l.PurchaseInvoiceItemOriginKey != null)
            .Select(l => l.PurchaseInvoiceItemOriginKey!.Value).Distinct().ToList();

        return await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);
    }

    private static PurchaseInvoiceItem OriginOf(PurchaseInvoiceItem item, Dictionary<Guid, PurchaseInvoiceItem> origins) =>
        item.PurchaseInvoiceItemOriginKey is { } key && origins.TryGetValue(key, out var origin)
            ? origin
            : throw new DefaultException($"O item {item.ItemCode} da devolução não aponta um item da entrada.");
}
