using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <param name="Direction">Nulo: as duas direções.</param>
/// <param name="CardCode">Nulo: todos os parceiros.</param>
public sealed record FinancialPositionFilter(FinancialDirection? Direction, string? CardCode, DateTime BaseDate);

/// <summary>
/// Carrega do banco o que o <see cref="FinancialPositionCalculator"/> precisa e o chama. Depois de selecionar por direção
/// e parceiro, EXPANDE para o grupo de composição inteiro (o provisório e todos os firmes/devoluções compostos nele, mesmo de
/// outro parceiro), porque a distribuição de R5/R7 e o composto de R8 dependem do grupo todo. Quem chama filtra o resultado
/// (parceiro, vencimento, saldo).
/// </summary>
public static class FinancialPositionQuery
{
    public static async Task<FinancialPosition> LoadAsync(AppDbContext ctx, FinancialPositionFilter f,
        FinancialPositionMode mode, CancellationToken ct)
    {
        var query = ctx.FinancialDocuments.AsNoTracking().AsQueryable();
        if (f.Direction is { } direction) query = query.Where(d => d.Direction == direction);
        if (!string.IsNullOrWhiteSpace(f.CardCode))
        {
            var cardCode = f.CardCode.Trim();
            query = query.Where(d => d.CardCode == cardCode);
        }

        var selected = await query.Select(d => new { d.Key, d.Nature }).ToListAsync(ct);
        var selectedKeys = selected.Select(d => d.Key).ToList();

        // Provisórios do grupo: os selecionados, os apontados pelo ledger dos compostos e os da tabela de composição.
        var provisionalKeys = selected.Where(d => d.Nature == FinancialDocumentNature.Provisional).Select(d => d.Key).ToHashSet();
        provisionalKeys.UnionWith(await ctx.FinancialSettlements.AsNoTracking()
            .Where(s => selectedKeys.Contains(s.FinancialDocumentKey) && s.CompositionKey != null && s.RelatedDocumentKey != null)
            .Select(s => s.RelatedDocumentKey!.Value).Distinct().ToListAsync(ct));
        provisionalKeys.UnionWith(await ctx.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => selectedKeys.Contains(c.ComposedDocumentKey))
            .Select(c => c.ProvisionalDocumentKey).Distinct().ToListAsync(ct));

        // Compostos de cada provisório do grupo (ledger e tabela).
        var provisionalList = provisionalKeys.ToList();
        var keys = selectedKeys.ToHashSet();
        keys.UnionWith(provisionalList);
        keys.UnionWith(await ctx.FinancialSettlements.AsNoTracking()
            .Where(s => s.CompositionKey != null && s.RelatedDocumentKey != null
                        && provisionalList.Contains(s.RelatedDocumentKey.Value))
            .Select(s => s.FinancialDocumentKey).Distinct().ToListAsync(ct));
        keys.UnionWith(await ctx.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => provisionalList.Contains(c.ProvisionalDocumentKey))
            .Select(c => c.ComposedDocumentKey).Distinct().ToListAsync(ct));

        var keyList = keys.ToList();
        var documents = await ctx.FinancialDocuments.AsNoTracking()
            .Where(d => keyList.Contains(d.Key)).ToListAsync(ct);
        var settlements = await ctx.FinancialSettlements.AsNoTracking()
            .Where(s => keyList.Contains(s.FinancialDocumentKey)).ToListAsync(ct);
        var compositions = await ctx.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => keyList.Contains(c.ProvisionalDocumentKey) || keyList.Contains(c.ComposedDocumentKey))
            .ToListAsync(ct);

        return FinancialPositionCalculator.Compute(
            new FinancialPositionInput(documents, settlements, compositions), f.BaseDate, mode);
    }
}
