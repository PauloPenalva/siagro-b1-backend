using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR e regras comuns aos relatórios de logística de venda. Os rótulos de situação,
/// tipo e origem são OS MESMOS de <c>siagro-b1-frontend/webapp/model/formatter.ts</c>: o PDF não
/// pode chamar de "Aberta" a carga que a tela chama de "Carregada".
/// </summary>
public static class LogisticsReportText
{
    public const string MissingPeriod = "Informe o período.";
    public const string PlannedPrefix = "(planejado) ";

    // Arrays (e não listas) para o EF traduzir o Contains em IN.
    private static readonly ShipmentLoadStatus[] DefaultLoadStatuses =
    [
        ShipmentLoadStatus.Open, ShipmentLoadStatus.PartiallyInvoiced, ShipmentLoadStatus.Invoiced,
        ShipmentLoadStatus.Planned, ShipmentLoadStatus.Returned, ShipmentLoadStatus.Completed,
        ShipmentLoadStatus.InTransshipment, ShipmentLoadStatus.Discharged, ShipmentLoadStatus.RefusalPending,
    ];

    private static readonly ReleaseStatus[] DefaultReleaseStatuses =
        [ReleaseStatus.Pending, ReleaseStatus.Actived, ReleaseStatus.Completed, ReleaseStatus.Paused];

    private static readonly StorageTransactionsStatus[] DefaultTransactionStatuses =
    [
        StorageTransactionsStatus.Pending, StorageTransactionsStatus.Confirmed,
        StorageTransactionsStatus.Invoiced, StorageTransactionsStatus.Returned,
    ];

    public static string? Validate(LogisticsReportRequest request) =>
        ReportText.ValidatePeriod(request.FromDate, request.ToDate, MissingPeriod);

    public static ShipmentLoadStatus[] EffectiveLoadStatuses(IReadOnlyCollection<ShipmentLoadStatus>? requested) =>
        Effective(requested, DefaultLoadStatuses);

    public static ReleaseStatus[] EffectiveReleaseStatuses(IReadOnlyCollection<ReleaseStatus>? requested) =>
        Effective(requested, DefaultReleaseStatuses);

    public static StorageTransactionsStatus[] EffectiveTransactionStatuses(
        IReadOnlyCollection<StorageTransactionsStatus>? requested) =>
        Effective(requested, DefaultTransactionStatuses);

    private static T[] Effective<T>(IReadOnlyCollection<T>? requested, T[] defaults) where T : struct, Enum =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : defaults;

    public static string LoadStatusText(ShipmentLoadStatus status) => status switch
    {
        ShipmentLoadStatus.Planned => "Planejada",
        ShipmentLoadStatus.Open => "Carregada",
        ShipmentLoadStatus.PartiallyInvoiced => "Faturada Parcial",
        ShipmentLoadStatus.Invoiced => "Faturada",
        ShipmentLoadStatus.Cancelled => "Cancelada",
        ShipmentLoadStatus.Returned => "Devolvida",
        ShipmentLoadStatus.Completed => "Concluída",
        ShipmentLoadStatus.InTransshipment => "Em Transbordo",
        ShipmentLoadStatus.Discharged => "Descarregada",
        ShipmentLoadStatus.RefusalPending => "Recusa aguardando NF-e",
        _ => status.ToString(),
    };

    public static string LoadTypeText(ShipmentLoadType type) =>
        type == ShipmentLoadType.Removal ? "Remoção" : "Normal";

    public static string ReleaseStatusText(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Pending => "Pendente",
        ReleaseStatus.Actived => "Ativo",
        ReleaseStatus.Completed => "Finalizado",
        ReleaseStatus.Cancelled => "Cancelado",
        ReleaseStatus.Paused => "Pausado",
        _ => status.ToString(),
    };

    public static string OriginText(ReleaseOrigin origin) => origin switch
    {
        ReleaseOrigin.OwnershipTransfer => "Transferência",
        ReleaseOrigin.SalesReturn => "Devolução",
        ReleaseOrigin.Transshipment => "Transbordo",
        _ => "Compra",
    };

    public static string TransactionStatusText(StorageTransactionsStatus status) => status switch
    {
        StorageTransactionsStatus.Pending => "Pendente",
        StorageTransactionsStatus.Confirmed => "Confirmado",
        StorageTransactionsStatus.Cancelled => "Cancelado",
        StorageTransactionsStatus.Invoiced => "Faturado",
        StorageTransactionsStatus.Returned => "Devolvido",
        _ => status.ToString(),
    };

    /// <summary>
    /// "Situação: todas" / "Situação: todas, exceto Cancelada" / lista na ordem numérica do enum.
    /// A tela envia o padrão já marcado, então o padrão também chega aqui como lista explícita.
    /// </summary>
    public static string StatusFilter<T>(IReadOnlyCollection<T> effective, Func<T, string> label, T cancelled)
        where T : struct, Enum
    {
        var chosen = effective.ToHashSet();
        var all = Enum.GetValues<T>();

        if (all.All(chosen.Contains))
            return "Situação: todas";

        if (!chosen.Contains(cancelled) && all.Where(s => !s.Equals(cancelled)).All(chosen.Contains))
            return $"Situação: todas, exceto {label(cancelled)}";

        return "Situação: " + string.Join(", ", chosen.OrderBy(s => Convert.ToInt32(s)).Select(label));
    }

    /// <summary>
    /// Cliente(s) da carga: nomes distintos das notas de saída VIVAS (cancelada não conta; status
    /// nulo é Pendente). Sem nota viva, o cliente do planejamento com o prefixo "(planejado) " (à frente, para o reticências da célula não esconder) — campo
    /// informativo da Logística, ver <see cref="ShipmentLoad.CardCode"/>. Sem carga, vazio.
    /// </summary>
    public static string LoadCustomers(ShipmentLoad? load)
    {
        if (load is null)
            return "";

        var live = LiveInvoices(load);
        if (live.Count > 0)
            return ReportText.JoinDistinct(live.Select(i => ReportText.NameOrCode(i.CardCode, i.CardName)));

        var planned = ReportText.NameOrCode(load.CardCode, load.CardName);
        return planned.Length == 0 ? "" : PlannedPrefix + planned;
    }

    /// <summary>Mesmo critério de <see cref="LoadCustomers"/>, para o filtro de Cliente.</summary>
    public static bool LoadHasCustomer(ShipmentLoad? load, string cardCode)
    {
        if (load is null)
            return false;

        var live = LiveInvoices(load);
        return live.Count > 0 ? live.Any(i => i.CardCode == cardCode) : load.CardCode == cardCode;
    }

    /// <summary>Nome do cliente filtrado, para a linha de filtros; null se não aparecer em nenhuma carga.</summary>
    public static string? CustomerName(IEnumerable<ShipmentLoad?> loads, string? cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return null;

        foreach (var load in loads)
        {
            if (load is null)
                continue;

            var live = LiveInvoices(load);
            var invoice = live.FirstOrDefault(i => i.CardCode == cardCode);
            if (invoice is not null)
                return ReportText.NameOrCode(invoice.CardCode, invoice.CardName);

            if (live.Count == 0 && load.CardCode == cardCode)
                return ReportText.NameOrCode(load.CardCode, load.CardName);
        }

        return null;
    }

    /// <summary>
    /// Linha de filtros: período, filial, produto, situação e os filtros próprios do relatório.
    /// Descrições ausentes caem para o código digitado.
    /// </summary>
    public static string BuildFilters(
        string periodLabel,
        LogisticsReportRequest request,
        string? branchName,
        string? productName,
        string statusFilter,
        IEnumerable<string> extra)
    {
        var parts = new List<string> { ReportText.Period(periodLabel, request.FromDate, request.ToDate) };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {ReportText.Describe(productName, request.ItemCode)}");

        parts.Add(statusFilter);
        parts.AddRange(extra);

        return ReportText.JoinFilters(parts);
    }

    /// <summary>Nome da filial pela tabela local BRANCHS (existe nos dois modos).</summary>
    public static async Task<string?> BranchNameAsync(AppDbContext context, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var branch = await context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code);
        return branch is null ? null : ReportText.BranchName(branch, code);
    }

    private static List<SalesInvoice> LiveInvoices(ShipmentLoad load) =>
        load.Invoices.Where(i => (i.InvoiceStatus ?? InvoiceStatus.Pending) != InvoiceStatus.Cancelled).ToList();
}
