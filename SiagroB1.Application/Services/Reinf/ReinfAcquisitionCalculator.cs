using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Fiscal.Reinf.Events;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Reinf;

/// <summary>
/// Apura as aquisições de produção rural do R-2055 num período (AAAA-MM). A seleção espelha o
/// relatório de Funrural retido (<c>FunruralWithholdingReportService</c>): linhas de entrada que não
/// são devolução, com Funrural retido e emissão no mês; devoluções de compra (<c>IsNfeReturn</c>)
/// que apontam para a linha são abatidas na origem, qualquer que seja a data. Agrupa por filial ×
/// produtor. Linha zerada por devolução total continua no resultado (vira exclusão adiante).
/// </summary>
public class ReinfAcquisitionCalculator(AppDbContext context)
{
    private static readonly InvoiceStatus[] Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Returned];
    private static readonly NfeStatus[] VoidNfeStatuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

    public async Task<(IReadOnlyList<ReinfAcquisitionRow> Rows, IReadOnlyList<ReinfIssue> Issues)> CalculateAsync(
        string taxIdRoot, string period, CancellationToken ct)
    {
        var root = ReinfFormat.Root(taxIdRoot);
        var from = DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture);
        var toExclusive = from.AddMonths(1);

        var branches = (await context.Branchs.AsNoTracking().ToListAsync(ct))
            .Select(b => (b.Code, TaxId: ReinfFormat.Digits(b.TaxId)))
            .Where(b => b.Code != null && b.TaxId.Length == 14 && b.TaxId.StartsWith(root, StringComparison.Ordinal))
            .ToDictionary(b => b.Code!, b => b.TaxId);
        var branchCodes = branches.Keys.ToArray();

        var items = await context.PurchaseInvoicesItems.AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => i.PurchaseInvoice!.InvoiceType != PurchaseInvoiceType.Return)
            .Where(i => i.FunruralInssValue + i.FunruralRatValue + i.FunruralSenarValue > 0)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => Statuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
            .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus))
            .Where(i => branchCodes.Contains(i.PurchaseInvoice!.BranchCode))
            .ToListAsync(ct);

        var originKeys = items.Select(i => i.Key).ToArray();
        var returns = originKeys.Length == 0
            ? []
            : await context.PurchaseInvoicesItems.AsNoTracking()
                .Include(i => i.PurchaseInvoice)
                .Where(i => i.PurchaseInvoiceItemOriginKey != null && originKeys.Contains(i.PurchaseInvoiceItemOriginKey))
                .Where(i => i.PurchaseInvoice != null && i.PurchaseInvoice.IsNfeReturn)
                .Where(i => Statuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
                .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus))
                .ToListAsync(ct);
        var returnsByOrigin = returns.GroupBy(r => r.PurchaseInvoiceItemOriginKey).ToDictionary(g => g.Key!, g => g.ToList());

        var cardCodes = items.Select(i => i.PurchaseInvoice!.CardCode).Distinct().ToArray();
        var partners = await context.BusinessPartners.AsNoTracking()
            .Where(p => cardCodes.Contains(p.CardCode)).ToDictionaryAsync(p => p.CardCode, ct);

        var usageCodes = items.Where(i => i.UsageCode != null).Select(i => i.UsageCode!.Value).Distinct().ToArray();
        var usages = await context.Usages.AsNoTracking()
            .Where(u => usageCodes.Contains(u.Code)).ToDictionaryAsync(u => u.Code, ct);

        var rates = await context.RuralContributionRates.AsNoTracking()
            .Where(r => r.Scheme == RuralContributionScheme.Standard)
            .OrderByDescending(r => r.StartDate).ToListAsync(ct);

        var issues = new List<ReinfIssue>();
        var reportedUsages = new HashSet<int>();
        var reportedDates = new HashSet<DateOnly>();
        var lines = new List<Line>();

        foreach (var item in items)
        {
            var invoice = item.PurchaseInvoice!;
            partners.TryGetValue(invoice.CardCode, out var partner);
            Usage? usage = item.UsageCode is { } uc && usages.TryGetValue(uc, out var u) ? u : null;
            var payroll = partner?.RuralPayrollOption ?? false;
            var senarOnly = usage?.RuralContributionMode == RuralContributionMode.SenarOnly && !payroll;

            var gross = item.FunruralBase;
            var inss = item.FunruralInssValue;
            var rat = item.FunruralRatValue;
            var senar = item.FunruralSenarValue;
            if (returnsByOrigin.TryGetValue(item.Key, out var rets))
            {
                foreach (var r in rets)
                {
                    gross -= r.FunruralBase;
                    inss -= r.FunruralInssValue;
                    rat -= r.FunruralRatValue;
                    senar -= r.FunruralSenarValue;
                }
            }

            string? process = null, suspension = null;
            decimal inssNot = 0m, ratNot = 0m;
            if (senarOnly)
            {
                process = string.IsNullOrWhiteSpace(usage!.RuralJudicialProcessNumber) ? null : usage.RuralJudicialProcessNumber.Trim();
                suspension = string.IsNullOrWhiteSpace(usage.RuralJudicialSuspensionCode) ? null : usage.RuralJudicialSuspensionCode.Trim();
                if (process is null && reportedUsages.Add(usage.Code))
                    issues.Add(new ReinfIssue($"Natureza {usage.Code} em 'Somente SENAR' sem processo judicial.", usage.Code.ToString(CultureInfo.InvariantCulture)));

                var date = DateOnly.FromDateTime(invoice.IssueDate!.Value);
                var rate = rates.FirstOrDefault(r => r.StartDate <= date);
                if (rate is null)
                {
                    var text = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    if (reportedDates.Add(date))
                        issues.Add(new ReinfIssue($"Sem alíquota de Funrural vigente em {text}.", text));
                }
                else
                {
                    inssNot = Math.Round(gross * rate.InssRate / 100m, 2, MidpointRounding.AwayFromZero);
                    ratNot = Math.Round(gross * rate.RatRate / 100m, 2, MidpointRounding.AwayFromZero);
                }
            }

            lines.Add(new Line(invoice.BranchCode!, invoice.CardCode, invoice.CardName ?? partner?.CardName ?? "", partner,
                senarOnly ? 4 : 1, process, suspension, gross, inss, rat, senar, inssNot, ratNot));
        }

        var rows = new List<ReinfAcquisitionRow>();
        var reportedPartners = new HashSet<string>();
        foreach (var g in lines.GroupBy(l => (Establishment: branches[l.BranchCode], l.CardCode)))
        {
            var first = g.First();
            var (type, producerTaxId) = Producer(first.Partner);
            if (type == 0 && reportedPartners.Add(first.CardCode))
                issues.Add(new ReinfIssue(producerTaxId.Length == 14
                    ? $"Produtor {first.CardCode} é pessoa jurídica sem CPF do produtor rural; informe o CPF no cadastro."
                    : $"Produtor {first.CardCode} sem CPF/CNPJ válido.", first.CardCode));

            var details = g.GroupBy(l => (l.Indicator, l.Process))
                .OrderBy(d => d.Key.Indicator).ThenBy(d => d.Key.Process ?? "", StringComparer.Ordinal)
                .Select(d => new R2055Detail(
                    d.Key.Indicator, d.Sum(x => x.Gross), d.Sum(x => x.Inss), d.Sum(x => x.Rat), d.Sum(x => x.Senar),
                    d.Key.Process, d.First().Suspension, d.Sum(x => x.InssNot), d.Sum(x => x.RatNot)))
                .ToList();

            var payroll = first.Partner?.RuralPayrollOption ?? false;
            rows.Add(new ReinfAcquisitionRow(
                g.Key.Establishment, first.BranchCode, first.CardCode, first.CardName, type, producerTaxId, payroll,
                details, details.Sum(d => d.Gross), details.Sum(d => d.Inss), details.Sum(d => d.Rat), details.Sum(d => d.Senar),
                Hash(g.Key.Establishment, type, producerTaxId, payroll, details)));
        }

        return (rows.OrderBy(r => r.EstablishmentTaxId, StringComparer.Ordinal)
            .ThenBy(r => r.CardCode, StringComparer.Ordinal).ToList(), issues);
    }

    /// <summary>
    /// tpInscProd: 2 = CPF. CNPJ com CPF do produtor informa o CPF; CNPJ sem CPF do produtor é pendência (0),
    /// nunca tpInscProd 1. 0 = inválido.
    /// </summary>
    private static (int Type, string TaxId) Producer(BusinessPartner? partner)
    {
        var taxId = ReinfFormat.Digits(partner?.TaxId);
        var cpf = ReinfFormat.Digits(partner?.RuralProducerCpf);
        if (taxId.Length == 14 && cpf.Length == 11) return (2, cpf);
        if (taxId.Length == 11) return (2, taxId);
        return (0, taxId);
    }

    private static string Hash(string establishment, int type, string producer, bool payroll, IEnumerable<R2055Detail> details)
    {
        var sb = new StringBuilder();
        sb.Append(establishment).Append('|').Append(type).Append('|').Append(producer).Append('|').Append(payroll ? 'S' : 'N');
        foreach (var d in details)
        {
            sb.Append(';').Append(d.AcquisitionIndicator)
                .Append('|').Append(d.Gross.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(d.Inss.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(d.Rat.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(d.Senar.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(d.JudicialProcess).Append('|').Append(d.SuspensionCode)
                .Append('|').Append(d.InssNotWithheld.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(d.RatNotWithheld.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private sealed record Line(
        string BranchCode, string CardCode, string CardName, BusinessPartner? Partner, int Indicator,
        string? Process, string? Suspension, decimal Gross, decimal Inss, decimal Rat, decimal Senar,
        decimal InssNot, decimal RatNot);
}
