using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributos da linha do Documento de Entrada pela natureza (spec §6.2). Só age com a regra ativa e documento
/// Pendente. Calcula a emissão própria — a entrada Normal (natureza de Entrada, CFOP 1xxx/2xxx) e a devolução de
/// compra criada pelo Devolver (natureza de Saída, CFOP 5xxx/6xxx, conferida contra a linha comprada) — e, desde a
/// spec terceiro-chave (D1), o documento de terceiro Normal, igual à entrada própria. A devolução do cliente
/// (terceiro + Devolução) fica exatamente como chegou.
/// </summary>
public class PurchaseInvoicesTaxApplyService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService,
    IbsCbsRatesService ibsCbsRatesService,
    RuralContributionRatesService ruralContributionRatesService)
{
    private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);

    public Task<bool> IsBranchActiveAsync(string? branchCode) => gate.IsActiveAsync(branchCode);

    /// <summary>
    /// A contra-nota fica de fora (spec 2026-10-10 D5/D7): a fotografia é a da escrituração, copiada, e o Funrural é da
    /// escrituração. Fora da regra, o Update zera o Funrural que vier no corpo.
    /// </summary>
    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.ProducerInvoiceKey is null
        && (invoice.IssuerType == DocumentIssuerType.Own
            // Spec 2026-10-14 (regra 7): o complemento de valor próprio calcula como a entrada própria (base = valor).
            // Spec 2026-10-17: o complemento de ICMS também (o motor resolve o CFOP; o ICMS é o informado).
            && (invoice.InvoiceType is PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement or PurchaseInvoiceType.IcmsComplement
                || IsOwnNfeReturn(invoice))
            || invoice.IssuerType == DocumentIssuerType.ThirdParty
            && invoice.InvoiceType is PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement or PurchaseInvoiceType.IcmsComplement)
        && await gate.IsActiveAsync(invoice.BranchCode);

    /// <summary>Devolução de compra criada pelo "Devolver": sai com NF-e própria de saída.</summary>
    public static bool IsOwnNfeReturn(PurchaseInvoice invoice) =>
        invoice.InvoiceType == PurchaseInvoiceType.Return && invoice.IsNfeReturn;

    /// <summary>
    /// Regras dos complementos — "Complemento de valor" (spec 2026-10-13 D11) e "Complemento de ICMS" (spec 2026-10-17) —
    /// e do valor de complemento nos demais tipos.
    /// </summary>
    public Task EnsureComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        PurchaseInvoiceComplement.EnsureAsync(invoice, lines, usageService, gate, db.Context);

    /// <summary>
    /// Spec 2026-10-17 §1: a natureza da linha decide se o contrato é exigido ou recusado (só STANDALONE; tipo Normal e,
    /// pela D3 dos complementos, os dois complementos).
    /// </summary>
    public Task EnsureUsageContractRuleAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        PurchaseInvoiceLineGuard.EnsureUsageContractRuleAsync(usageService, gate.IsStandalone, invoice, lines);

    /// <summary>Spec 2026-10-17 §2: a linha de outra compra aceita qualquer produto ativo (só STANDALONE).</summary>
    public Task<bool> IsOtherPurchaseLineAsync(PurchaseInvoice invoice, PurchaseInvoiceItem line) =>
        PurchaseInvoiceLineGuard.IsOtherPurchaseLineAsync(usageService, gate.IsStandalone, invoice, line);

    /// <summary>Spec 2026-10-17 §2: retenções de serviço do cabeçalho (zeradas fora de "outra compra"; só STANDALONE).</summary>
    public async Task ApplyWithholdingsAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem>? lines = null)
    {
        lines ??= invoice.Items.ToList();
        var otherPurchase = await PurchaseInvoiceLineGuard.IsOtherPurchaseDocumentAsync(
            usageService, gate.IsStandalone, invoice, lines);
        await PurchaseInvoiceWithholdings.ApplyAsync(invoice, usageService, otherPurchase, lines);
    }

    /// <summary>
    /// Spec 2026-10-17 §2: DIFAL e FCP da linha (fotografia informativa; fora de NetPayable, GrandTotal e NF-e). Zera os seis
    /// campos quando as condições deixam de valer. Sem alíquota interna da UF a gravação passa com zeros: só a confirmação recusa.
    /// </summary>
    public async Task ApplyDifalAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem>? lines = null)
    {
        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            return;

        foreach (var o in await EvaluateDifalAsync(invoice, lines))
            PurchaseInvoiceDifal.Store(o.Line, o.Result);
    }

    /// <summary>Calcula o DIFAL de cada linha sem gravar; <c>Error</c> preenchido = a confirmação deve recusar.</summary>
    internal async Task<List<PurchaseInvoiceDifal.LineOutcome>> EvaluateDifalAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem>? lines = null) =>
        await PurchaseInvoiceDifal.EvaluateAsync(
            db.Context, usageService, businessPartnerService, gate.IsStandalone, invoice, lines ?? invoice.Items.ToList());

    /// <summary>Confirmação: recusa se faltar a alíquota da UF e regrava o DIFAL de cada linha (a UF pode ter sido cadastrada depois da gravação).</summary>
    public async Task RefreshDifalForConfirmAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        var outcomes = await EvaluateDifalAsync(invoice, lines);
        if (outcomes.FirstOrDefault(o => o.Error is not null) is { } refused)
            throw new DefaultException(refused.Error!);

        foreach (var o in outcomes)
            PurchaseInvoiceDifal.Store(o.Line, o.Result);
    }

    public async Task ApplyAsync(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            return;

        // Pendente com NF-e Autorizada (estorno ou confirmação que falhou), Cancelada (cancelamento local que
        // falhou) ou Em processamento: o que foi ao
        // XML não se recalcula — alíquota ou natureza novas descolariam a linha da nota, e a devolução seria
        // conferida contra valores que não são os autorizados.
        if (invoice.NfeStatus is NfeStatus.Authorized or NfeStatus.Cancelled or NfeStatus.Processing)
            return;

        var lines = items.ToList();
        var icmsComplement = invoice.InvoiceType == PurchaseInvoiceType.IcmsComplement;

        // Complemento de ICMS (spec 2026-10-17 D4): base e valor são os informados — guardados antes do motor, que os zera.
        var typed = icmsComplement
            ? lines.ToDictionary(l => l, l => (l.IcmsBase, l.IcmsValue), ReferenceEqualityComparer.Instance)
            : null;

        if (!await IsActiveForAsync(invoice))
        {
            // Fora da regra (filial STANDALONE que não emite NF-e pelo Siagro; o próprio já foi recusado) o motor não roda,
            // mas a alíquota e o CST do complemento de ICMS existem mesmo assim.
            if (icmsComplement)
                foreach (var line in lines)
                    PurchaseInvoiceIcmsComplementTaxes.Apply(
                        line, PurchaseInvoiceIcmsComplementTaxes.CstFor(null), line.IcmsBase, line.IcmsValue);
            return;
        }

        var ownReturn = IsOwnNfeReturn(invoice);
        var branch = await _lines.LoadBranchAsync(invoice.BranchCode);
        // Um GetById por documento: a UF decide o CFOP e o cadastro de produtor rural decide a contribuição social.
        var partner = await TaxLineCalculator.LoadPartnerAsync(businessPartnerService, invoice.CardCode);
        var supplierState = TaxLineCalculator.PartnerState(partner);
        var inState = string.Equals(branch.StateCode, supplierState, StringComparison.OrdinalIgnoreCase);

        // CT-e: o tributo segue as UFs da PRESTAÇÃO (início → fim), não a UF da transportadora; o CFOP segue a UF de
        // início contra a da filial (spec 2026-10-17).
        var cte = PurchaseInvoiceCteRules.IsCte(invoice);
        bool? cfopInState = null;
        var originState = supplierState;
        var destinationState = branch.StateCode!;
        if (cte)
        {
            var start = (invoice.FreightStartState ?? string.Empty).Trim().ToUpperInvariant();
            var end = (invoice.FreightEndState ?? string.Empty).Trim().ToUpperInvariant();
            if (start.Length == 0 || end.Length == 0)
                throw new DefaultException(PurchaseInvoiceCteRules.MissingStatesMessage);
            inState = start == end;
            originState = start;
            destinationState = end;
            cfopInState = string.Equals(start, branch.StateCode, StringComparison.OrdinalIgnoreCase);
        }

        // Devolução: IBS/CBS pelas alíquotas da DATA DA ENTRADA — ela anula o tributo daquela operação.
        var rateDate = ownReturn
            ? await OriginDateAsync(invoice)
            : DateOnly.FromDateTime(invoice.IssueDate ?? DateTime.Today);
        var origins = ownReturn ? await LoadOriginItemsAsync(lines) : new Dictionary<Guid, PurchaseInvoiceItem>();
        // Complemento de ICMS próprio: CST e CFOP vêm do item da nota original (D4).
        var complemented = icmsComplement && invoice.IssuerType == DocumentIssuerType.Own
            ? await PurchaseInvoiceComplement.LoadOriginalsAsync(db.Context, lines)
            : new Dictionary<Guid, PurchaseInvoiceItem>();
        var usages = new Dictionary<int, UsageModel>();
        var ruralRates = new Dictionary<RuralContributionScheme, RuralContributionRates?>();
        var number = 0;

        foreach (var item in lines)
        {
            number++;

            if (string.IsNullOrWhiteSpace(item.ItemCode))
                throw new DefaultException($"Informe o produto do item {number}.");

            var usage = await ResolveUsageAsync(item, usages, ownReturn);

            // A mercadoria vem do FORNECEDOR para a filial; a devolução repete a operação da compra, para
            // reproduzir a alíquota creditada (12% de BA→SP, e não 7% de SP→BA). Base = total geral da linha
            // (spec 2026-10-05 D3).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.GrandTotal, inState, originState, destinationState,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: !ownReturn, CfopInState: cfopInState));

            TaxLineCalculator.Apply(item, usage, line);

            if (icmsComplement)
            {
                // O motor rodou com valor 0: resolveu o CFOP (o da natureza, no terceiro) e zerou o resto. Sem Funrural.
                var original = item.ComplementedItemKey is { } originalKey && complemented.TryGetValue(originalKey, out var o)
                    ? o
                    : null;
                if (!string.IsNullOrWhiteSpace(original?.Cfop))
                    item.Cfop = original.Cfop;

                var (typedBase, typedValue) = typed![item];
                PurchaseInvoiceIcmsComplementTaxes.Apply(
                    item, PurchaseInvoiceIcmsComplementTaxes.CstFor(original?.CstIcms), typedBase, typedValue);
            }
            else if (ownReturn)
            {
                var origin = OriginOf(item, origins);

                // Origem sem fotografia (entrada de terceiro antiga digitada, ou linha incluída depois da importação
                // antes da spec terceiro-chave): não há com o que conferir. A origem calculada pelo motor é conferida.
                if (!string.IsNullOrWhiteSpace(origin.CstIcms))
                    NfeReturnConference.Ensure(item, origin, usage.Name, "compra");

                // Funrural da devolução: as alíquotas da COMPRA, não as da natureza de devolução — quem comprou só
                // com SENAR devolve só com SENAR, mesmo com o cadastro alterado depois (spec 2026-10-09 §5).
                PurchaseInvoiceFunrural.Apply(item, origin.FunruralInssRate, origin.FunruralRatRate, origin.FunruralSenarRate);
            }
            else
            {
                // Contribuição social do produtor rural (spec 2026-10-18 §4.4): a natureza diz o modo, o cadastro do
                // parceiro diz se retém e em que regime, e a tabela vigente na data do documento dá as alíquotas.
                var rates = await ResolveRuralContributionAsync(usage, partner, rateDate, ruralRates);
                if (rates is { } r)
                    PurchaseInvoiceFunrural.Apply(item, r.Inss, r.Rat, r.Senar);
                else
                    PurchaseInvoiceFunrural.Clear(item);
            }
        }
    }

    /// <summary>
    /// Alíquotas da linha pela regra; a tabela é lida só para o regime que a linha consulta e uma vez por documento
    /// (cache por regime — a data é a do documento).
    /// </summary>
    private async Task<RuralContributionRates?> ResolveRuralContributionAsync(
        UsageModel usage, BusinessPartnerModel partner, DateOnly rateDate,
        Dictionary<RuralContributionScheme, RuralContributionRates?> cache)
    {
        var mode = usage.RuralContributionMode;
        if (!RuralContributionRule.Withholds(mode, partner.IsRuralProducer, partner.RuralProducerType))
            return null;

        var scheme = RuralContributionRule.SchemeFor(mode, partner.RuralPayrollOption);
        RuralContributionRates? effective = null;
        if (scheme is { } s)
        {
            if (!cache.TryGetValue(s, out effective))
            {
                var row = await ruralContributionRatesService.GetEffectiveAsync(s, rateDate);
                effective = row is null ? null : new RuralContributionRates(row.InssRate, row.RatRate, row.SenarRate);
                cache[s] = effective;
            }
        }

        return RuralContributionRule.Resolve(
            mode, partner.IsRuralProducer, partner.RuralProducerType, partner.RuralPayrollOption,
            scheme == RuralContributionScheme.Standard ? effective : null,
            scheme == RuralContributionScheme.PayrollOption ? effective : null,
            rateDate);
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
