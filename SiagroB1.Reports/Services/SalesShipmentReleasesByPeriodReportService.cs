using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Liberações de entrega de contratos de VENDA, agrupadas pelo produto + UM do contrato. A
/// liberação não tem código próprio: a linha mostra o do contrato. Cliente, vendedor, produto e
/// região são snapshots do SALES_CONTRACTS (tabela local, FK obrigatória); região por LEFT JOIN em
/// LOGISTIC_REGIONS. Consumido = ShippedQuantity; saldo = <see cref="SalesShipmentRelease.AvailableQuantity"/>.
/// </summary>
public class SalesShipmentReleasesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<SalesShipmentReleaseRowDto>> BuildRowsAsync(SalesShipmentReleasesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var query = db.Context.SalesShipmentReleases
            .AsNoTracking()
            .Include(r => r.SalesContract).ThenInclude(c => c!.LogisticRegion)
            .Where(r => r.ReleaseDate >= from && r.ReleaseDate < toExclusive)
            .Where(r => statuses.Contains(r.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(r => r.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(r => r.SalesContract!.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(r => r.SalesContract!.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(r => r.SalesContract!.CardCode == request.CardCode);

        if (request.AgentCode is { } agent)
            query = query.Where(r => r.SalesContract!.AgentCode == agent);

        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            query = query.Where(r => r.SalesContract!.LogisticRegionCode == request.LogisticRegionCode);

        var releases = (await query.ToListAsync()).Where(r => r.SalesContract is not null).ToList();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            releases, r => r.SalesContract!.ItemCode, r => r.SalesContract!.ItemName, r => r.SalesContract!.UnitOfMeasureCode);

        return releases
            .OrderBy(r => groupOf(r), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ReleaseDate)
            .ThenBy(r => r.SalesContract!.Code ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.DeliveryLocationCode, StringComparer.Ordinal)
            .ThenBy(r => r.ReleasedQuantity)
            .ThenBy(r => r.Key)
            .Select(r => ToRow(r, groupOf(r)))
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesShipmentReleasesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(first?.Customer, request.CardCode)}");
        if (request.AgentCode is { } agent)
            extra.Add($"Vendedor: {ReportText.Describe(first?.Agent, agent.ToString())}");
        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            extra.Add($"Região: {ReportText.Describe(first?.Region, request.LogisticRegionCode)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da liberação",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "SalesShipmentReleasesByPeriod.frx", rows, "SalesShipmentReleases", "SalesShipmentReleases", parameters);
    }

    private static SalesShipmentReleaseRowDto ToRow(SalesShipmentRelease r, string group)
    {
        var contract = r.SalesContract!;

        return new SalesShipmentReleaseRowDto
        {
            Group = group,
            ReleaseDate = ReportText.Date(r.ReleaseDate),
            DeliveryDeadline = ReportText.Date(contract.DeliveryEndDate),
            Contract = contract.Code ?? "",
            Customer = ReportText.Partner(contract.CardCode, contract.CardName),
            Agent = ReportText.NameOrCode(contract.AgentCode?.ToString(), contract.AgentName),
            DeliveryLocation = ReportText.NameOrCode(r.DeliveryLocationCode, r.DeliveryLocationName),
            Region = ReportText.NameOrCode(contract.LogisticRegionCode, contract.LogisticRegion?.Name),
            Status = LogisticsReportText.ReleaseStatusText(r.Status),
            UnitOfMeasure = contract.UnitOfMeasureCode,
            ReleasedQuantity = r.ReleasedQuantity,
            ConsumedQuantity = r.ShippedQuantity,
            BalanceQuantity = r.AvailableQuantity,
        };
    }
}
