using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Cálculo dos tributos da linha pela natureza de operação (NF-e STANDALONE, spec §6–§7).
///
/// Só age com a regra ativa (<see cref="TaxCalculationGate"/>), em documento Normal ou na devolução própria (InvoiceType Return + IsNfeReturn) e enquanto o
/// documento está Pendente: fora disso a linha fica exatamente como chegou — é isso que mantém a
/// Yokotobi (SAPB1) e a MH Agro (chave desligada) intocadas.
///
/// Toda lacuna de cadastro é <see cref="DefaultException"/> com mensagem de negócio: com a linha
/// travada, nota sem imposto em silêncio é o pior resultado possível.
/// </summary>
public class SalesInvoicesTaxApplyService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService,
    IbsCbsRatesService ibsCbsRatesService)
{
    private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);

    public async Task<bool> IsActiveForAsync(SalesInvoice invoice) =>
        (invoice.InvoiceType == SalesInvoiceType.Normal || IsOwnNfeReturn(invoice))
        && await gate.IsActiveAsync(invoice.BranchCode);

    /// <summary>Devolução criada pelo "Devolver" da venda: sai com NF-e própria de entrada (spec §7).</summary>
    public static bool IsOwnNfeReturn(SalesInvoice invoice) =>
        invoice.InvoiceType == SalesInvoiceType.Return && invoice.IsNfeReturn;

    public async Task ApplyAsync(SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceStatus is not (null or InvoiceStatus.Pending))
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var lines = items.ToList();
        var ownReturn = IsOwnNfeReturn(invoice);
        var branch = await _lines.LoadBranchAsync(invoice.BranchCode);
        var customerState = await TaxLineCalculator.LoadPartnerStateAsync(businessPartnerService, invoice.CardCode);
        var inState = string.Equals(branch.StateCode, customerState, StringComparison.OrdinalIgnoreCase);

        // Devolução: IBS/CBS pelas alíquotas da DATA DA VENDA — ela anula o tributo daquela operação,
        // inclusive se a vigência virou entre a venda e a devolução.
        var rateDate = ownReturn
            ? await OriginDateAsync(invoice)
            : DateOnly.FromDateTime(invoice.InvoiceDate ?? DateTime.Today);
        var origins = ownReturn ? await LoadOriginItemsAsync(lines) : new Dictionary<Guid, SalesInvoiceItem>();
        var usages = new Dictionary<int, UsageModel>();

        foreach (var item in lines)
        {
            var usage = await ResolveUsageAsync(item, usages, ownReturn);
            // Venda e devolução de venda: a mercadoria sai da filial para o cliente (na devolução, a operação
            // é a da venda, que ela anula).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.Total, inState, branch.StateCode!, customerState,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: ownReturn));

            TaxLineCalculator.Apply(item, usage, line);

            if (ownReturn)
                NfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name, "venda");
        }
    }

    private async Task<DateOnly> OriginDateAsync(SalesInvoice invoice)
    {
        var date = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == invoice.SalesInvoiceOriginKey)
            .Select(i => i.InvoiceDate)
            .FirstOrDefaultAsync();

        return DateOnly.FromDateTime(date ?? throw new DefaultException("A devolução está sem a venda de origem."));
    }

    private async Task<Dictionary<Guid, SalesInvoiceItem>> LoadOriginItemsAsync(IEnumerable<SalesInvoiceItem> lines)
    {
        var keys = lines.Where(l => l.SalesInvoiceItemOriginKey != null)
            .Select(l => l.SalesInvoiceItemOriginKey!.Value).Distinct().ToList();

        return await db.Context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);
    }

    private static SalesInvoiceItem OriginOf(SalesInvoiceItem item, Dictionary<Guid, SalesInvoiceItem> origins) =>
        item.SalesInvoiceItemOriginKey is { } key && origins.TryGetValue(key, out var origin)
            ? origin
            : throw new DefaultException($"O item {item.ItemCode} da devolução não aponta um item da venda.");

    private async Task<UsageModel> ResolveUsageAsync(
        SalesInvoiceItem item, Dictionary<int, UsageModel> cache, bool ownReturn)
    {
        UsageModel usage;

        if (item.UsageCode is { } code)
        {
            if (!cache.TryGetValue(code, out usage!))
            {
                usage = await usageService.GetByIdAsync(code)
                        ?? throw new DefaultException("Natureza de operação não encontrada.");
                cache[code] = usage;
            }
        }
        else if (ownReturn)
        {
            // A linha da devolução nasce com a natureza de devolução da linha vendida; sem ela, a
            // natureza padrão (de SAÍDA) daria CFOP de venda numa nota de entrada.
            throw new DefaultException($"O item {item.ItemCode} da devolução está sem natureza de devolução.");
        }
        else
        {
            usage = (await usageService.GetAllAsync()).FirstOrDefault(u => u is { IsDefault: true, Inactive: false })
                    ?? throw new DefaultException(
                        $"O item {item.ItemCode} está sem natureza de operação e não há natureza padrão cadastrada.");
        }

        if (ownReturn && usage.Direction != UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de saída e não pode ser usada na devolução.");

        if (!ownReturn && usage.Direction == UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada no documento de saída.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }
}
