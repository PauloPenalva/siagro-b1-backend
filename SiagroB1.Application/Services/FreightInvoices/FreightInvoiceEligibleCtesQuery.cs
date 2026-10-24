using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>CT-e pendente de fatura de frete.</summary>
public record FreightInvoiceEligibleCte(
    Guid TransportDocumentKey, int Number, int Series, string AccessKey, DateTime IssuedAt, string Route,
    decimal WeightKg, decimal Amount, Guid? FreightOrderKey, string? TakerCardCode = null);

/// <summary>
/// CT-e elegíveis à fatura: autorizado, do tomador e da filial, emitido no período, fora de fatura ativa, com título a receber
/// ativo (mesma definição de FinancialDocumentsTransportService.ActiveTitleCodesAsync: origem TransportDocument, não
/// cancelado) e nenhuma parcela baixada ou composta. Valor = soma do saldo em aberto das parcelas.
/// </summary>
public class FreightInvoiceEligibleCtesQuery(IUnitOfWork db)
{
    /// <param name="takerCardCode">Nulo lista os elegíveis de todos os tomadores (geração em lote).</param>
    /// <param name="ignoreInvoiceKey">Itens desta fatura não contam como "já faturado" (re-salvar um rascunho).</param>
    public async Task<IReadOnlyList<FreightInvoiceEligibleCte>> ListAsync(
        string branchCode, string? takerCardCode, DateTime? from, DateTime? to, Guid? ignoreInvoiceKey = null)
    {
        var context = db.Context;
        var query = context.TransportDocuments.AsNoTracking()
            .Where(c => c.BranchCode == branchCode && c.Status == CteStatus.Authorized && c.IssuedAt != null)
            .Where(c => c.Parties.Any(p => (int)p.Role == c.TakerRole
                && (takerCardCode == null ? p.CardCode != null : p.CardCode == takerCardCode)))
            .Where(c => !context.FreightInvoiceItems.Any(i =>
                i.TransportDocumentKey == c.Key && i.IsActive && (ignoreInvoiceKey == null || i.InvoiceKey != ignoreInvoiceKey)));
        if (from is { } f) query = query.Where(c => c.IssuedAt >= f.Date);
        if (to is { } t) query = query.Where(c => c.IssuedAt < t.Date.AddDays(1));

        var ctes = await query.Include(c => c.Quantities).Include(c => c.Parties).OrderBy(c => c.IssuedAt).ThenBy(c => c.Number).ToListAsync();
        if (ctes.Count == 0) return [];

        var keys = ctes.Select(c => c.Key).ToList();
        var titles = (await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument && d.Status != FinancialDocumentStatus.Canceled
                            && d.OriginKey != null && keys.Contains(d.OriginKey.Value))
                .ToListAsync())
            .GroupBy(d => d.OriginKey!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var codes = ctes.SelectMany(c => new[] { c.StartMunicipalityCode, c.EndMunicipalityCode })
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        var names = await context.Municipalities.AsNoTracking().Where(m => codes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, m => m.Name);

        var result = new List<FreightInvoiceEligibleCte>();
        foreach (var cte in ctes)
        {
            if (!titles.TryGetValue(cte.Key, out var list) || list.Count == 0) continue;
            if (list.Any(d => d.SettledAmount > 0m || d.ComposedAmount > 0m)) continue;
            result.Add(new FreightInvoiceEligibleCte(
                cte.Key, cte.Number ?? 0, cte.Series, cte.AccessKey ?? "", cte.IssuedAt!.Value,
                $"{Place(cte.StartMunicipalityCode, cte.StartState, names)} → {Place(cte.EndMunicipalityCode, cte.EndState, names)}",
                TransportDocumentWeight.GrossKg(cte), list.Sum(d => d.OpenAmount), cte.FreightOrderKey,
                cte.Parties.FirstOrDefault(p => (int)p.Role == cte.TakerRole)?.CardCode));
        }
        return result;
    }

    private static string Place(string? code, string? state, Dictionary<string, string> names)
    {
        var name = code != null && names.TryGetValue(code, out var n) ? n : code;
        return string.IsNullOrEmpty(state) ? name ?? "" : $"{name}/{state}";
    }
}
