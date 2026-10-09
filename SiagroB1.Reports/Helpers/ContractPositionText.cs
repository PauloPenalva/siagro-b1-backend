using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR e regras comuns aos relatórios de posição de contratos. Situação e tipo usam OS
/// MESMOS rótulos de <c>siagro-b1-frontend/webapp/model/formatter.ts</c>
/// (<c>formatContractStatus</c>, <c>formatContractType</c>).
/// </summary>
public static class ContractPositionText
{
    public const string Overdue = "Vencido";
    public const string NoDeadline = "Sem prazo";

    /// <summary>
    /// DeliveryEndDate é NOT NULL no banco: contrato "sem prazo" só existe como data vazia
    /// (0001-01-01, ou 1900-01-01 vinda de carga legada). Tudo antes de 1901 é "sem prazo".
    /// </summary>
    public static readonly DateTime NoDeadlineBefore = new(1901, 1, 1);

    private static readonly ContractStatus[] DefaultStatuses = [ContractStatus.Approved];

    /// <summary>Situação vazia = só Aprovado (posição viva).</summary>
    public static ContractStatus[] EffectiveStatuses(IReadOnlyCollection<ContractStatus>? requested) =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : DefaultStatuses;

    /// <summary>
    /// Array anulável para o EF traduzir o Contains em IN sobre a coluna anulável. Situação nula
    /// no banco é tratada como Rascunho (o padrão da entidade): entra quando Rascunho é pedido.
    /// </summary>
    public static ContractStatus?[] QueryStatuses(IReadOnlyCollection<ContractStatus>? requested)
    {
        var effective = EffectiveStatuses(requested);
        var query = effective.Select(s => (ContractStatus?)s).ToList();
        if (effective.Contains(ContractStatus.Draft))
            query.Add(null);
        return query.ToArray();
    }

    public static string StatusText(ContractStatus? status) => status switch
    {
        ContractStatus.Approved => "Aprovado",
        ContractStatus.Finished => "Finalizado",
        ContractStatus.Canceled => "Cancelado",
        ContractStatus.InApproval => "Em Aprovação",
        ContractStatus.Rejected => "Rejeitado",
        _ => "Rascunho",
    };

    public static string TypeText(ContractType type) =>
        type == ContractType.ToBeDetermined ? "PAF - Preço a Fixar" : "FIX - Preço Fixo";

    /// <summary>Sigla da coluna Tipo (a coluna é estreita; o rótulo inteiro vai na linha de filtros).</summary>
    public static string TypeShort(ContractType type) =>
        type == ContractType.ToBeDetermined ? "PAF" : "FIX";

    public static string SideText(ContractPositionSide side) => side switch
    {
        ContractPositionSide.Purchase => "Compra",
        ContractPositionSide.Sales => "Venda",
        _ => "Compra e venda",
    };

    public static bool HasDeadline(DateTime deliveryEndDate) => deliveryEndDate >= NoDeadlineBefore;

    /// <summary>Término da entrega; vazio quando o contrato não tem prazo.</summary>
    public static string DeliveryEnd(DateTime deliveryEndDate) =>
        HasDeadline(deliveryEndDate) ? ReportText.Date(deliveryEndDate) : "";

    public static string StatusFilter(IReadOnlyCollection<ContractStatus> effective) =>
        LogisticsReportText.StatusFilter(effective, s => StatusText(s), ContractStatus.Canceled);

    /// <summary>
    /// Linha de filtros: data da posição, lado (só no relatório 1), filtros informados e situação.
    /// Descrições de produto e parceiro saem das posições lidas (snapshots); sem resultado, o código.
    /// </summary>
    public static string BuildFilters(
        DateTime today,
        ContractPositionReportRequest request,
        ContractPositionSide? side,
        string? branchName,
        IReadOnlyCollection<ContractPosition> positions)
    {
        var parts = new List<string> { $"Posição em: {ReportText.Date(today)}" };

        if (side is { } chosenSide)
            parts.Add($"Lado: {SideText(chosenSide)}");

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var product = positions.FirstOrDefault(p => p.ItemCode == request.ItemCode && !string.IsNullOrWhiteSpace(p.ItemName));
            parts.Add($"Produto: {(product is null ? request.ItemCode : ReportText.Product(product.ItemCode, product.ItemName))}");
        }

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            parts.Add($"Safra: {request.HarvestSeasonCode}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
        {
            var partner = positions.FirstOrDefault(p => p.CardCode == request.CardCode);
            parts.Add($"Parceiro: {ReportText.Describe(partner?.CardName?.Trim(), request.CardCode)}");
        }

        if (request.Type is { } type)
            parts.Add($"Tipo: {TypeText(type)}");

        if (request.DeliveryEndDateUntil is { } until)
            parts.Add($"Término da entrega até: {ReportText.Date(until)}");

        parts.Add(StatusFilter(EffectiveStatuses(request.Statuses)));

        return ReportText.JoinFilters(parts);
    }

    /// <summary>
    /// Ordem dentro de uma seção: previsão de pagamento (sem previsão por último), término da
    /// entrega (sem prazo por último), código (ordinal).
    /// </summary>
    public static IOrderedEnumerable<ContractPosition> InSectionOrder(IEnumerable<ContractPosition> positions) =>
        positions
            .OrderBy(p => p.StandardCashFlowDate is null)
            .ThenBy(p => p.StandardCashFlowDate)
            .ThenBy(p => !HasDeadline(p.DeliveryEndDate))
            .ThenBy(p => p.DeliveryEndDate)
            .ThenBy(p => p.Code, StringComparer.Ordinal);
}
