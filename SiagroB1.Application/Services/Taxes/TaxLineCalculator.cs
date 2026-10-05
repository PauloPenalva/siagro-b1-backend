using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <param name="OriginState">UF de onde a mercadoria sai (filial na venda e na devolução de venda; fornecedor na compra e na devolução de compra).</param>
/// <param name="IncomingCfop">Lê os CFOPs de entrada da natureza (1xxx/2xxx) em vez dos de saída.</param>
public sealed record TaxLineRequest(
    UsageModel Usage, string? ItemCode, decimal Amount, bool InState, string OriginState, string DestinationState,
    TaxRegime Regime, DateOnly RateDate, bool IncomingCfop);

public sealed record TaxLineResult(string Cfop, Item Product, TaxCalculationResult Taxes);

/// <summary>
/// Uma linha calculada pela natureza (SP1 §6): CFOP pelo sentido e pela UF, regras de ICMS/PIS/COFINS/IBS-CBS
/// da natureza, produto com NCM e origem, alíquota IBS/CBS vigente. Comum ao documento de saída e ao de
/// entrada; quem decide QUAL natureza, quais UFs e qual data é o serviço de cada documento.
/// </summary>
public class TaxLineCalculator(IUnitOfWork db, IbsCbsRatesService ibsCbsRatesService)
{
    public async Task<TaxLineResult> CalculateAsync(TaxLineRequest request)
    {
        var product = await LoadProductAsync(request.ItemCode);
        var cfop = ResolveCfop(request.Usage, request.InState, request.IncomingCfop);
        var icms = ResolveIcmsRule(request.Usage, request.InState, request.Regime);
        var pisCofins = ResolvePisCofinsRule(request.Usage);
        var (ibsCbs, rates) = await ResolveIbsCbsAsync(request.Usage, request.RateDate);

        var taxes = TaxCalculator.Calculate(new TaxCalculationInput(
            request.Amount, request.InState, request.OriginState, request.DestinationState, request.Regime,
            product.GoodsOrigin!.Value, icms, pisCofins, ibsCbs, rates));

        return new TaxLineResult(cfop, product, taxes);
    }

    /// <summary>Grava na linha a natureza, o CFOP, NCM/origem do produto, as flags e a fotografia.</summary>
    public static void Apply(INfeTaxedLine line, UsageModel usage, TaxLineResult result)
    {
        line.UsageCode = usage.Code;
        line.UsageName = usage.Name;
        line.Cfop = result.Cfop;
        line.Ncm = result.Product.Ncm;
        line.GoodsOrigin = result.Product.GoodsOrigin;
        line.MovesFiscalInventory = usage.MovesFiscalInventory;
        line.CreatesFinancialDocument = usage.CreatesFinancialDocument;

        TaxSnapshot.Write(line, result.Taxes);
    }

    public async Task<Branch> LoadBranchAsync(string? branchCode)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} do documento não encontrada.");

        if (branch.TaxRegime is null || string.IsNullOrWhiteSpace(branch.StateCode))
            throw new DefaultException(
                $"Filial {branch.Code} está sem regime tributário ou UF. " +
                "Complete o cadastro da filial antes de emitir o documento.");

        return branch;
    }

    public static async Task<string> LoadPartnerStateAsync(IBusinessPartnerService partners, string cardCode)
    {
        var partner = await partners.GetByIdAsync(cardCode)
                      ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

        var state = SalesInvoicesCfopResolveService.ResolvePartnerState(partner);

        return string.IsNullOrWhiteSpace(state)
            ? throw new DefaultException($"Parceiro {cardCode} está sem UF no endereço de faturamento.")
            : state;
    }

    private async Task<Item> LoadProductAsync(string? itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new DefaultException("Informe o produto do item.");

        var product = await db.Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.ItemCode == itemCode)
                      ?? throw new DefaultException($"Produto {itemCode} não encontrado no cadastro.");

        if (string.IsNullOrWhiteSpace(product.Ncm))
            throw new DefaultException($"Produto {itemCode} está sem NCM cadastrado.");

        if (product.GoodsOrigin is null)
            throw new DefaultException($"Produto {itemCode} está sem origem da mercadoria cadastrada.");

        return product;
    }

    private static string ResolveCfop(UsageModel usage, bool inState, bool incoming)
    {
        var cfop = incoming
            ? inState ? usage.CfopIncomingInState : usage.CfopIncomingOutState
            : inState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;
        var kind = incoming ? "entrada" : "saída";

        return string.IsNullOrWhiteSpace(cfop)
            ? throw new DefaultException(inState
                ? $"Natureza de operação {usage.Name} está sem CFOP de {kind} dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de {kind} fora do estado.")
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
