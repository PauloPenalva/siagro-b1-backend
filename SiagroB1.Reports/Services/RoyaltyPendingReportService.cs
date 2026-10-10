// SiagroB1.Reports/Services/RoyaltyPendingReportService.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class RoyaltyPendingReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Label(RoyaltySituation s) => s switch
    {
        RoyaltySituation.Declared => "Declarada",
        RoyaltySituation.Tested => "Testada",
        RoyaltySituation.Conventional => "Convencional",
        RoyaltySituation.ParticipantWarehouse => "Armazém Participante",
        _ => s.ToString(),
    };

    public async Task<RoyaltyPendingData> BuildAsync(RoyaltyPendingRequest request)
    {
        var filter = new RoyaltyBalanceFilter
        {
            FromDate = request.FromDate, ToDate = request.ToDate,
            CardCode = request.CardCode, OriginCode = request.OriginCode, ItemCode = request.ItemCode,
            Situation = Enum.TryParse<RoyaltySituation>(request.Situation, out var s) ? s : null,
            StorageAddressCode = request.StorageAddressCode,
        };
        var items = await RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(db.Context), filter).ToListAsync();

        var rows = new List<RoyaltyPendingRowDto>();
        foreach (var bySituation in items.GroupBy(x => x.Situation).OrderBy(g => g.Key))
        {
            foreach (var byPartner in bySituation
                         .GroupBy(x => (x.CardCode, x.CardName))
                         .OrderBy(g => g.Key.CardName ?? g.Key.CardCode, StringComparer.CurrentCultureIgnoreCase))
            {
                foreach (var x in byPartner.OrderBy(x => x.TransactionDate).ThenBy(x => x.Code, StringComparer.Ordinal))
                    rows.Add(new RoyaltyPendingRowDto
                    {
                        Kind = RoyaltyPendingRowKind.Item,
                        Situation = Label(x.Situation),
                        Type = x.TransactionType == StorageTransactionType.Purchase ? "Compra" : "Recebimento",
                        Code = x.Code ?? "",
                        Date = x.TransactionDate?.ToString("dd/MM/yyyy", Culture) ?? "",
                        Partner = $"{x.CardCode} - {x.CardName}",
                        Origin = x.RoyaltyOriginCode == null ? "" : $"{x.RoyaltyOriginCode} - {x.RoyaltyOriginName}",
                        TestCode = x.RoyaltyTestCode ?? "",
                        NetWeight = x.NetWeight, WrittenOff = x.WrittenOffWeight, Balance = x.Balance,
                    });
                rows.Add(Subtotal(RoyaltyPendingRowKind.Partner, $"Total {byPartner.Key.CardName ?? byPartner.Key.CardCode}", byPartner));
            }
            rows.Add(Subtotal(RoyaltyPendingRowKind.Situation, $"Total {Label(bySituation.Key)}", bySituation));
        }

        return new RoyaltyPendingData(rows, items.Count,
            items.Sum(x => x.NetWeight), items.Sum(x => x.WrittenOffWeight), items.Sum(x => x.Balance));
    }

    private static RoyaltyPendingRowDto Subtotal(RoyaltyPendingRowKind kind, string label, IEnumerable<RoyaltyBalanceRow> rows)
    {
        var list = rows.ToList();
        return new RoyaltyPendingRowDto
        {
            Kind = kind, Partner = label,
            NetWeight = list.Sum(x => x.NetWeight), WrittenOff = list.Sum(x => x.WrittenOffWeight),
            Balance = list.Sum(x => x.Balance),
        };
    }

    public async Task<byte[]> ExecuteAsync(RoyaltyPendingRequest request)
    {
        var data = await BuildAsync(request);

        var filters = new List<string>
        {
            $"Período: {request.FromDate:dd/MM/yyyy} a {request.ToDate:dd/MM/yyyy}",
        };
        if (Enum.TryParse<RoyaltySituation>(request.Situation, out var s)) filters.Add($"Situação: {Label(s)}");
        if (!string.IsNullOrWhiteSpace(request.CardCode)) filters.Add($"Parceiro: {request.CardCode}");
        if (!string.IsNullOrWhiteSpace(request.OriginCode)) filters.Add($"Origem: {request.OriginCode}");
        if (!string.IsNullOrWhiteSpace(request.ItemCode)) filters.Add($"Produto: {request.ItemCode}");
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode)) filters.Add($"Lote: {request.StorageAddressCode}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = string.Join("  |  ", filters),
            [FastReportService.StandaloneParameter] = ErpMode.IsStandalone(configuration),
            ["pItemCount"] = data.ItemCount,
            ["pTotalNetWeight"] = data.TotalNetWeight,
            ["pTotalWrittenOff"] = data.TotalWrittenOff,
            ["pTotalBalance"] = data.TotalBalance,
        };

        return await reportService.GeneratePdfAsync(
            "RoyaltyPending.frx", data.Rows, "RoyaltyRows", "RoyaltyRows", parameters);
    }
}
