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
/// Só age com a regra ativa (<see cref="TaxCalculationGate"/>), em documento Normal e enquanto o
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
    public async Task<bool> IsActiveForAsync(SalesInvoice invoice) =>
        invoice.InvoiceType == SalesInvoiceType.Normal && await gate.IsActiveAsync(invoice.BranchCode);

    public async Task ApplyAsync(SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceStatus is not (null or InvoiceStatus.Pending))
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var branch = await LoadBranchAsync(invoice.BranchCode);
        var customerState = await LoadCustomerStateAsync(invoice.CardCode);
        var inState = string.Equals(branch.StateCode, customerState, StringComparison.OrdinalIgnoreCase);
        var issueDate = DateOnly.FromDateTime(invoice.InvoiceDate ?? DateTime.Today);
        var usages = new Dictionary<int, UsageModel>();

        foreach (var item in items)
        {
            var usage = await ResolveUsageAsync(item, usages);
            var product = await LoadProductAsync(item.ItemCode);
            var cfop = ResolveCfop(usage, inState);
            var icms = ResolveIcmsRule(usage, inState, branch.TaxRegime!.Value);
            var pisCofins = ResolvePisCofinsRule(usage);
            var (ibsCbs, rates) = await ResolveIbsCbsAsync(usage, issueDate);

            var result = TaxCalculator.Calculate(new TaxCalculationInput(
                item.Total, inState, branch.StateCode!, customerState, branch.TaxRegime!.Value,
                product.GoodsOrigin!.Value, icms, pisCofins, ibsCbs, rates));

            item.UsageCode = usage.Code;
            item.UsageName = usage.Name;
            item.Cfop = cfop;
            item.Ncm = product.Ncm;
            item.GoodsOrigin = product.GoodsOrigin;
            item.MovesFiscalInventory = usage.MovesFiscalInventory;
            item.CreatesFinancialDocument = usage.CreatesFinancialDocument;

            SalesInvoiceTaxSnapshot.Write(item, result);
        }
    }

    private async Task<Branch> LoadBranchAsync(string? branchCode)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} do documento não encontrada.");

        if (branch.TaxRegime is null || string.IsNullOrWhiteSpace(branch.StateCode))
            throw new DefaultException(
                $"Filial {branch.Code} está sem regime tributário ou UF. " +
                "Complete o cadastro da filial antes de emitir o documento.");

        return branch;
    }

    private async Task<string> LoadCustomerStateAsync(string cardCode)
    {
        var partner = await businessPartnerService.GetByIdAsync(cardCode)
                      ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

        var state = SalesInvoicesCfopResolveService.ResolvePartnerState(partner);

        return string.IsNullOrWhiteSpace(state)
            ? throw new DefaultException($"Parceiro {cardCode} está sem UF no endereço de faturamento.")
            : state;
    }

    private async Task<UsageModel> ResolveUsageAsync(SalesInvoiceItem item, Dictionary<int, UsageModel> cache)
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
        else
        {
            usage = (await usageService.GetAllAsync()).FirstOrDefault(u => u is { IsDefault: true, Inactive: false })
                    ?? throw new DefaultException(
                        $"O item {item.ItemCode} está sem natureza de operação e não há natureza padrão cadastrada.");
        }

        if (usage.Direction == UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada no documento de saída.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }

    private async Task<Item> LoadProductAsync(string itemCode)
    {
        var product = await db.Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.ItemCode == itemCode)
                      ?? throw new DefaultException($"Produto {itemCode} não encontrado no cadastro.");

        if (string.IsNullOrWhiteSpace(product.Ncm))
            throw new DefaultException($"Produto {itemCode} está sem NCM cadastrado.");

        if (product.GoodsOrigin is null)
            throw new DefaultException($"Produto {itemCode} está sem origem da mercadoria cadastrada.");

        return product;
    }

    private static string ResolveCfop(UsageModel usage, bool inState)
    {
        var cfop = inState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;

        return string.IsNullOrWhiteSpace(cfop)
            ? throw new DefaultException(inState
                ? $"Natureza de operação {usage.Name} está sem CFOP de saída dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de saída fora do estado.")
            : cfop;
    }

    private static IcmsRule ResolveIcmsRule(UsageModel usage, bool inState, TaxRegime regime)
    {
        var rule = inState
            ? new IcmsRule(usage.IcmsInStateCst, usage.IcmsInStateCsosn, usage.IcmsInStateRate,
                usage.IcmsInStateBaseReduction, usage.IcmsInStateDeferral, usage.IcmsInStateBenefitCode)
            : new IcmsRule(usage.IcmsOutStateCst, usage.IcmsOutStateCsosn, null,
                usage.IcmsOutStateBaseReduction, usage.IcmsOutStateDeferral, usage.IcmsOutStateBenefitCode);

        var csosn = FiscalCodes.UsesCsosn(regime);
        var code = csosn ? rule.Csosn : rule.Cst;

        if (string.IsNullOrWhiteSpace(code))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} está sem {(csosn ? "CSOSN" : "CST")} de ICMS " +
                $"{(inState ? "dentro do estado" : "fora do estado")}. " +
                "Configure a tributação no cadastro de Naturezas de Operação.");

        return rule;
    }

    private static PisCofinsRule ResolvePisCofinsRule(UsageModel usage)
    {
        if (string.IsNullOrWhiteSpace(usage.PisCst) || string.IsNullOrWhiteSpace(usage.CofinsCst))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} está sem CST de PIS/COFINS. " +
                "Configure a tributação no cadastro de Naturezas de Operação.");

        return new PisCofinsRule(usage.PisCst, usage.PisRate, usage.CofinsCst, usage.CofinsRate,
            usage.ExcludeIcmsFromPisCofinsBase);
    }

    private async Task<(IbsCbsRule? rule, IbsCbsRates? rates)> ResolveIbsCbsAsync(UsageModel usage, DateOnly issueDate)
    {
        if (string.IsNullOrWhiteSpace(usage.IbsCbsCst))
            return (null, null);

        var rule = new IbsCbsRule(usage.IbsCbsCst, usage.IbsCbsClassCode ?? string.Empty,
            usage.IbsRateReduction, usage.CbsRateReduction);

        var rate = await ibsCbsRatesService.GetEffectiveAsync(issueDate)
                   ?? throw new DefaultException(
                       $"Não há alíquota de IBS/CBS vigente em {issueDate:dd/MM/yyyy}. " +
                       "Cadastre-a em Naturezas de Operação > Alíquotas IBS/CBS.");

        return (rule, new IbsCbsRates(rate.CbsRate, rate.IbsStateRate, rate.IbsMunicipalRate));
    }
}
