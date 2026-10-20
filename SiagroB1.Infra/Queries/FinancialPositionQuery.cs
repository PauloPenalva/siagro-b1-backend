using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <param name="Direction">Nulo: as duas direções.</param>
/// <param name="CardCode">Nulo: todos os parceiros.</param>
/// <param name="DueDateFrom">Opcional: só restringe a SELEÇÃO; o grupo de composição traz membros fora do período.</param>
/// <param name="DueDateTo">Idem.</param>
/// <param name="SettlementDateFrom">Opcional: a SELEÇÃO só mantém documentos com ao menos uma baixa em [SettlementDateFrom, BaseDate].</param>
public sealed record FinancialPositionFilter(FinancialDirection? Direction, string? CardCode, DateTime BaseDate,
    DateTime? DueDateFrom = null, DateTime? DueDateTo = null, DateTime? SettlementDateFrom = null);

/// <summary>
/// Carrega do banco o que o <see cref="FinancialPositionCalculator"/> precisa e o chama. Depois de selecionar por direção
/// e parceiro, EXPANDE para o grupo de composição inteiro (o provisório e todos os firmes/devoluções compostos nele, mesmo de
/// outro parceiro), porque a distribuição de R5/R7 e o composto de R8 dependem do grupo todo. Quem chama filtra o resultado
/// (parceiro, vencimento, saldo).
/// </summary>
public static class FinancialPositionQuery
{
    public static async Task<FinancialPosition> LoadAsync(AppDbContext ctx, FinancialPositionFilter f,
        FinancialPositionMode mode, CancellationToken ct) =>
        FinancialPositionCalculator.Compute(await LoadInputAsync(ctx, f, ct), f.BaseDate, mode);

    /// <summary>Só a carga (seleção + expansão do grupo), para quem precisa calcular a posição em várias datas &lt;= BaseDate.</summary>
    public static async Task<FinancialPositionInput> LoadInputAsync(AppDbContext ctx, FinancialPositionFilter f,
        CancellationToken ct)
    {
        // Pré-filtro só da seleção (evita ler a tabela inteira); a expansão abaixo não usa estes filtros.
        var dayAfterBase = f.BaseDate.Date.AddDays(1);
        var query = ctx.FinancialDocuments.AsNoTracking()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled && d.DocumentDate < dayAfterBase);
        if (f.DueDateFrom is { } dueFrom)
        {
            var from = dueFrom.Date;
            query = query.Where(d => d.DueDate >= from);
        }
        if (f.DueDateTo is { } dueTo)
        {
            var toExclusive = dueTo.Date.AddDays(1);
            query = query.Where(d => d.DueDate < toExclusive);
        }
        // Só sem parceiro: com parceiro a seleção já é pequena, e o firme pode ter sido pago pelo provisório de OUTRO parceiro
        // (a baixa não está nele), que só entra pela expansão do grupo.
        if (f.SettlementDateFrom is { } settledFrom && string.IsNullOrWhiteSpace(f.CardCode))
        {
            var sFrom = settledFrom.Date;
            query = query.Where(d => ctx.FinancialSettlements.Any(s => s.FinancialDocumentKey == d.Key
                                                                       && s.SettlementDate >= sFrom && s.SettlementDate < dayAfterBase));
        }
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

        return new FinancialPositionInput(documents, settlements, compositions);
    }
}
