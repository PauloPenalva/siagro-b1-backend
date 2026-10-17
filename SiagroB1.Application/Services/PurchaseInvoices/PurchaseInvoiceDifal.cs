using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// DIFAL e FCP da linha do Documento de Entrada (spec 2026-10-17 §2). Só STANDALONE, documento de TERCEIRO, tipo
/// Normal e fora da devolução; emitente de outra UF (e não "EX"); natureza de uso e consumo ou ativo. Fora disso o
/// resultado é <see cref="DifalResult.None"/>. DIFAL e FCP são informativos: não entram em NetPayable, GrandTotal nem na NF-e.
/// </summary>
internal static class PurchaseInvoiceDifal
{
    public sealed record LineOutcome(PurchaseInvoiceItem Line, DifalResult Result, string? Error);

    public static void Store(PurchaseInvoiceItem line, DifalResult r)
    {
        line.DifalBase = r.Base;
        line.DifalInternalRate = r.InternalRate;
        line.DifalInterstateRate = r.InterstateRate;
        line.DifalValue = r.Value;
        line.FcpRate = r.FcpRate;
        line.FcpValue = r.FcpValue;
    }

    /// <remarks>Alíquota interestadual: o <c>IcmsRate</c> da linha (calculado pelo motor, informado ou do XML) quando maior que zero; senão <c>Resolve</c>.</remarks>
    public static async Task<List<LineOutcome>> EvaluateAsync(
        AppDbContext ctx, IUsage usages, IBusinessPartnerService partners, bool standalone,
        PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> lines)
    {
        var list = lines.ToList();
        var none = list.Select(l => new LineOutcome(l, DifalResult.None, null)).ToList();

        if (!standalone
            || invoice.InvoiceType != PurchaseInvoiceType.Normal
            || invoice.IssuerType != DocumentIssuerType.ThirdParty
            || invoice.IsNfeReturn)
            return none;

        var branch = await ctx.Branchs.AsNoTracking().Where(b => b.Code == invoice.BranchCode)
            .Select(b => new { b.StateCode, b.StateRegistration }).FirstOrDefaultAsync();

        // DIFAL só existe para filial contribuinte do ICMS (spec §1): sem inscrição estadual, nada a apurar.
        if (branch is null || string.IsNullOrWhiteSpace(branch.StateRegistration))
            return none;

        var branchState = branch.StateCode?.Trim().ToUpperInvariant();
        var partner = await partners.GetByIdAsync(invoice.CardCode);
        var supplierState = partner is null
            ? null
            : SalesInvoicesCfopResolveService.ResolvePartnerState(partner)?.Trim().ToUpperInvariant();

        var unresolved = string.IsNullOrEmpty(branchState) || string.IsNullOrEmpty(supplierState);
        if (!unresolved && (supplierState == "EX" || supplierState == branchState))
            return none;

        var outcomes = new List<LineOutcome>();
        var purposes = new Dictionary<int, PurchasePurpose>();

        foreach (var line in list)
        {
            if (line.UsageCode is not { } code)
            {
                outcomes.Add(new LineOutcome(line, DifalResult.None, null));
                continue;
            }

            if (!purposes.TryGetValue(code, out var purpose))
            {
                purpose = (await usages.GetByIdAsync(code))?.PurchasePurpose ?? PurchasePurpose.ResaleOrInput;
                purposes[code] = purpose;
            }

            if (purpose is not (PurchasePurpose.UseAndConsumption or PurchasePurpose.FixedAsset))
            {
                outcomes.Add(new LineOutcome(line, DifalResult.None, null));
                continue;
            }

            if (unresolved)
            {
                outcomes.Add(new LineOutcome(line, DifalResult.None,
                    $"Não foi possível identificar a UF do emitente {invoice.CardCode} ou da filial para calcular o DIFAL: confira o endereço de faturamento."));
                continue;
            }

            var rates = await IcmsInternalRateResolver.ResolveAsync(ctx, line.ItemCode, branchState!);
            if (rates is null)
            {
                outcomes.Add(new LineOutcome(line, DifalResult.None,
                    $"Cadastre a alíquota interna de ICMS da UF {branchState} em ICMS por UF."));
                continue;
            }

            var interstate = line.IcmsRate > 0m
                ? line.IcmsRate
                : InterstateIcmsRate.Resolve(supplierState!, branchState!, await GoodsOriginAsync(ctx, line));

            try
            {
                var result = DifalCalculator.Calculate(new DifalInput(
                    line.GrandTotal, interstate, line.IcmsValue, rates.Value.InternalRate, rates.Value.FcpRate,
                    rates.Value.BaseRule));
                outcomes.Add(new LineOutcome(line, result, null));
            }
            catch (ArgumentException)
            {
                outcomes.Add(new LineOutcome(line, DifalResult.None,
                    $"A alíquota interna somada ao FCP da UF {branchState} deve ser menor que 100%. Revise em ICMS por UF."));
            }
        }

        return outcomes;
    }

    private static async Task<byte> GoodsOriginAsync(AppDbContext ctx, PurchaseInvoiceItem line) =>
        line.GoodsOrigin
        ?? await ctx.Items.AsNoTracking().Where(i => i.ItemCode == line.ItemCode)
            .Select(i => i.GoodsOrigin).FirstOrDefaultAsync()
        ?? 0;
}
