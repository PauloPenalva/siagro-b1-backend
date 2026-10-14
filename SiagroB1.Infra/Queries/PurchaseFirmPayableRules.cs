using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Regras puras do título firme de compra (spec 2026-10-13, títulos firmes, regras 4 e 6–7). Moram no Infra porque o
/// PurchaseFiscalReconciliationCleanupInterceptor — que não enxerga a Application — precisa cancelar ou recusar o firme
/// quando a NF ou o romaneio somem; os serviços da Application usam as mesmas. Síncronas de propósito: o interceptor
/// tem caminho síncrono, e as consultas são poucas e por chave.
/// </summary>
public static class PurchaseFirmPayableRules
{
    public const string LinkUndoneReason = "Vínculo fiscal desfeito";

    /// <summary>Usuário do cancelamento em cascata (NF/romaneio cancelado): o interceptor não sabe quem é.</summary>
    public const string SystemUserName = "Sistema";

    /// <summary>Regra 4: a parte da linha que cabe ao vínculo, sem o Funrural retido. Multiplica antes de dividir.</summary>
    public static decimal Amount(decimal linkedKg, decimal lineKg, decimal lineGrandTotal, decimal lineFunrural) =>
        lineKg <= 0m ? 0m : decimal.Round(linkedKg * (lineGrandTotal - lineFunrural) / lineKg, 2, MidpointRounding.ToEven);

    public static string ComposedRefusal(string? firmCode, string? provisionalCode) =>
        $"O título {firmCode} do vínculo já está composto no título {provisionalCode}: desfaça a composição antes de alterar a conciliação.";

    public static string SettledRefusal(string? firmCode) =>
        $"O título {firmCode} do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.";

    /// <summary>Situação do firme na tela da conciliação.</summary>
    public static string SituationName(FinancialDocumentStatus status, bool composed) =>
        status == FinancialDocumentStatus.Canceled ? "Cancelado" : composed ? "Composto" : "Solto";

    /// <summary>
    /// Firmes/devoluções ATIVOS dos vínculos, rastreados. Confere o status de novo em memória: o mesmo SaveChanges pode
    /// já tê-los cancelado (estorno da alocação seguido do cancelamento do romaneio).
    /// </summary>
    public static List<FinancialDocument> ActiveFirmsOf(DbContext context, IReadOnlyCollection<Guid> linkKeys)
    {
        if (linkKeys.Count == 0) return [];

        return context.Set<FinancialDocument>()
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.OriginKey != null && linkKeys.Contains(d.OriginKey.Value) &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToList()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
    }

    /// <summary>Provisório em que o título está composto, contando linhas pendentes (Added conta, Deleted não). Null = solto.</summary>
    public static Guid? ProvisionalOf(DbContext context, Guid composedKey)
    {
        var tracked = context.ChangeTracker.Entries<FinancialDocumentComposition>()
            .Where(e => e.Entity.ComposedDocumentKey == composedKey)
            .ToList();

        var added = tracked.FirstOrDefault(e => e.State == EntityState.Added);
        if (added is not null) return added.Entity.ProvisionalDocumentKey;

        var deleted = tracked.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Key).ToHashSet();

        return context.Set<FinancialDocumentComposition>().AsNoTracking()
            .Where(c => c.ComposedDocumentKey == composedKey)
            .Select(c => new { c.Key, c.ProvisionalDocumentKey })
            .AsEnumerable()
            .Where(c => !deleted.Contains(c.Key))
            .Select(c => (Guid?)c.ProvisionalDocumentKey)
            .FirstOrDefault();
    }

    /// <summary>Regras 6–7: "solto" = não composto e sem baixa (dação e encontro de contas contam como baixa).</summary>
    public static void EnsureLoose(DbContext context, FinancialDocument firm)
    {
        if (ProvisionalOf(context, firm.Key) is { } provisionalKey)
        {
            var provisionalCode = context.Set<FinancialDocument>().AsNoTracking()
                .Where(d => d.Key == provisionalKey)
                .Select(d => d.Code)
                .FirstOrDefault();

            throw new DefaultException(ComposedRefusal(firm.Code, provisionalCode));
        }

        if (firm.SettledAmount != 0m)
            throw new DefaultException(SettledRefusal(firm.Code));
    }

    /// <summary>
    /// Regra 7: vínculo desfeito → o firme solto é CANCELADO (nunca apagado); um não-solto recusa a operação inteira,
    /// antes de qualquer mudança. Enqueue-only. Grava os carimbos à mão: o Infra não enxerga
    /// FinancialDocumentsCancelService.Cancel.
    /// </summary>
    public static void CancelLooseFirms(DbContext context, IReadOnlyCollection<Guid> linkKeys, string userName)
    {
        var firms = ActiveFirmsOf(context, linkKeys);
        foreach (var firm in firms)
            EnsureLoose(context, firm);

        var now = DateTime.Now;
        foreach (var firm in firms)
        {
            firm.Status = FinancialDocumentStatus.Canceled;
            firm.CancellationReason = LinkUndoneReason;
            firm.CanceledAt = now;
            firm.CanceledBy = userName;
            firm.UpdatedAt = now;
            firm.UpdatedBy = userName;
        }
    }
}
