using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Contratos — Compra x Venda": posição atual dos contratos por produto + UM, com a seção de
/// compras, a de vendas e o saldo geral do bloco (compra − venda). Entregue e saldo vêm do razão
/// recalculado na hora (<see cref="ContractPositionData"/>), nunca do AllocatedVolume persistido.
/// Só snapshots de PURCHASE_CONTRACTS/SALES_CONTRACTS e a tabela local BRANCHS: funciona nos dois modos.
/// </summary>
public class ContractPositionReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string PurchaseSection = "Compras";
    public const string SalesSection = "Vendas";

    public async Task<List<ContractPositionRowDto>> BuildRowsAsync(ContractPositionRequest request) =>
        ToRows(await LoadAsync(request), request.Side);

    public Task<byte[]> ExecuteAsync(ContractPositionRequest request) => ExecuteAsync(request, DateTime.Today);

    /// <summary><paramref name="today"/> só entra na linha de filtros ("Posição em").</summary>
    public async Task<byte[]> ExecuteAsync(ContractPositionRequest request, DateTime today)
    {
        var positions = await LoadAsync(request);
        var rows = ToRows(positions, request.Side);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = ContractPositionText.BuildFilters(
                today,
                request,
                request.Side,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                positions),
            ["pGeneralLabel"] = GeneralLabel(request.Side),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ContractPosition.frx", rows, "ContractPositions", "ContractPositions", parameters);
    }

    public static string GeneralLabel(ContractPositionSide side) => side switch
    {
        ContractPositionSide.Purchase => "Saldo geral (compra):",
        ContractPositionSide.Sales => "Saldo geral (venda):",
        _ => "Saldo geral (compra - venda):",
    };

    private async Task<List<ContractPosition>> LoadAsync(ContractPositionRequest request)
    {
        var positions = new List<ContractPosition>();

        if (request.Side != ContractPositionSide.Sales)
            positions.AddRange(await ContractPositionData.PurchasePositionsAsync(
                db.Context, ContractPositionData.PurchaseQuery(db.Context, request)));

        if (request.Side != ContractPositionSide.Purchase)
            positions.AddRange(await ContractPositionData.SalesPositionsAsync(
                db.Context, ContractPositionData.SalesQuery(db.Context, request)));

        return positions;
    }

    private static List<ContractPositionRowDto> ToRows(List<ContractPosition> positions, ContractPositionSide side)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            positions, p => p.ItemCode, p => p.ItemName, p => Uom(p.UnitOfMeasureCode));

        return positions
            .GroupBy(p => (Group: groupOf(p), p.Side))
            .OrderBy(g => g.Key.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Group, StringComparer.Ordinal) // desempate: Compras/Vendas do grupo ficam contíguas
            .ThenBy(g => g.Key.Side) // Compras antes de Vendas
            .SelectMany(g => ContractPositionText.InSectionOrder(g).Select(p => ToRow(p, g.Key.Group, side)))
            .ToList();
    }

    /// <summary>"KG" e "kg " são a mesma UM: o snapshot do contrato pode vir com caixa/espaço diferente.</summary>
    private static string? Uom(string? code) => code?.Trim().ToUpperInvariant();

    private static ContractPositionRowDto ToRow(ContractPosition p, string group, ContractPositionSide side) => new()
    {
        Group = group,
        Section = p.Side == ContractPositionSide.Purchase ? PurchaseSection : SalesSection,
        Code = p.Code,
        CreationDate = ReportText.Date(p.CreationDate),
        Partner = ReportText.Partner(p.CardCode, p.CardName),
        HarvestSeason = p.HarvestSeasonCode,
        Type = ContractPositionText.TypeShort(p.Type),
        CashFlowDate = ReportText.Date(p.StandardCashFlowDate),
        DeliveryEndDate = ContractPositionText.DeliveryEnd(p.DeliveryEndDate),
        Status = ContractPositionText.StatusText(p.Status),
        UnitOfMeasure = Uom(p.UnitOfMeasureCode),
        Price = p.Price,
        ContractedQuantity = p.Contracted,
        DeliveredQuantity = p.Delivered,
        WashedOutQuantity = p.WashedOut,
        BalanceQuantity = p.Balance,
        SummedBalanceQuantity = Summed(p.Balance),
        // Saldo geral = compra − venda; com um lado só, o próprio saldo daquele lado.
        SignedBalanceQuantity = side == ContractPositionSide.Both && p.Side == ContractPositionSide.Sales
            ? -Summed(p.Balance)
            : Summed(p.Balance),
    };

    /// <summary>Saldo negativo (entregue além do contratado) aparece na linha, mas não entra nas somas.</summary>
    private static decimal Summed(decimal balance) => Math.Max(balance, 0m);
}
