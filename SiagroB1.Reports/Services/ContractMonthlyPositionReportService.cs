using System.Globalization;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Posição Comprado x Vendido por Mês": por produto + safra + UM, o contratado, o entregue e o
/// washout no cabeçalho do grupo, e o saldo a entregar distribuído pelo término da entrega:
/// Vencido (término antes de hoje), um mês por linha a partir do mês atual, e Sem prazo. O líquido
/// acumulado começa no Vencido. Saldo negativo entra como está (reduz o mês); contrato Finalizado,
/// Cancelado ou Rejeitado não tem mais o que entregar (<see cref="ContractPosition.ToDeliver"/>).
/// </summary>
public class ContractMonthlyPositionReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public Task<List<ContractMonthlyPositionRowDto>> BuildRowsAsync(ContractMonthlyPositionRequest request) =>
        BuildRowsAsync(request, DateTime.Today);

    /// <summary><paramref name="today"/> = data do servidor na emissão; os testes fixam a data.</summary>
    public async Task<List<ContractMonthlyPositionRowDto>> BuildRowsAsync(ContractMonthlyPositionRequest request, DateTime today) =>
        ToRows(await LoadAsync(request), today.Date);

    public Task<byte[]> ExecuteAsync(ContractMonthlyPositionRequest request) => ExecuteAsync(request, DateTime.Today);

    public async Task<byte[]> ExecuteAsync(ContractMonthlyPositionRequest request, DateTime today)
    {
        var positions = await LoadAsync(request);
        var rows = ToRows(positions, today.Date);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = ContractPositionText.BuildFilters(
                today,
                request,
                side: null,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                positions),
            ["pContractCount"] = positions.Count,
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ContractMonthlyPosition.frx", rows, "ContractMonthlyPositions", "ContractMonthlyPositions", parameters);
    }

    private async Task<List<ContractPosition>> LoadAsync(ContractMonthlyPositionRequest request)
    {
        var positions = await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, request));
        positions.AddRange(await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, request)));
        return positions;
    }

    private static List<ContractMonthlyPositionRowDto> ToRows(List<ContractPosition> positions, DateTime today)
    {
        // Sem a UM no resolver: o rótulo é "Produto (código) - Safra x - UM".
        var productOf = InvoiceItemGrouping.BuildGroupResolver(positions, p => p.ItemCode, p => p.ItemName, _ => null);

        return positions
            .GroupBy(p => $"{productOf(p)} - Safra {p.HarvestSeasonCode} - {p.UnitOfMeasureCode}")
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(g => GroupRows(g.Key, g.ToList(), today))
            .ToList();
    }

    private static IEnumerable<ContractMonthlyPositionRowDto> GroupRows(
        string group, List<ContractPosition> positions, DateTime today)
    {
        var purchases = positions.Where(p => p.Side == ContractPositionSide.Purchase).ToList();
        var sales = positions.Where(p => p.Side == ContractPositionSide.Sales).ToList();
        var contractedPurchase = purchases.Sum(p => p.Contracted);
        var contractedSales = sales.Sum(p => p.Contracted);
        var deliveredPurchase = purchases.Sum(p => p.Delivered);
        var deliveredSales = sales.Sum(p => p.Delivered);

        var buckets = new List<(string Label, List<ContractPosition> Items)>
        {
            // Vencido sai sempre: é onde o acumulado começa.
            (ContractPositionText.Overdue, positions
                .Where(p => ContractPositionText.HasDeadline(p.DeliveryEndDate) && p.DeliveryEndDate.Date < today)
                .ToList()),
        };

        buckets.AddRange(positions
            .Where(p => ContractPositionText.HasDeadline(p.DeliveryEndDate) && p.DeliveryEndDate.Date >= today)
            .GroupBy(p => new DateTime(p.DeliveryEndDate.Year, p.DeliveryEndDate.Month, 1))
            .OrderBy(m => m.Key)
            .Select(m => (m.Key.ToString("MM/yyyy", Culture), m.ToList())));

        var withoutDeadline = positions.Where(p => !ContractPositionText.HasDeadline(p.DeliveryEndDate)).ToList();
        if (withoutDeadline.Count > 0)
            buckets.Add((ContractPositionText.NoDeadline, withoutDeadline));

        var accumulated = 0m;
        foreach (var (label, items) in buckets)
        {
            var purchase = items.Where(p => p.Side == ContractPositionSide.Purchase).Sum(p => p.ToDeliver);
            var sale = items.Where(p => p.Side == ContractPositionSide.Sales).Sum(p => p.ToDeliver);
            accumulated += purchase - sale;

            yield return new ContractMonthlyPositionRowDto
            {
                Group = group,
                UnitOfMeasure = positions[0].UnitOfMeasureCode,
                Bucket = label,
                ContractedPurchase = contractedPurchase,
                ContractedSales = contractedSales,
                ContractedNet = contractedPurchase - contractedSales,
                DeliveredPurchase = deliveredPurchase,
                DeliveredSales = deliveredSales,
                DeliveredNet = deliveredPurchase - deliveredSales,
                WashedOutPurchase = purchases.Sum(p => p.WashedOut),
                PurchaseQuantity = purchase,
                SalesQuantity = sale,
                NetQuantity = purchase - sale,
                AccumulatedQuantity = accumulated,
            };
        }
    }
}
