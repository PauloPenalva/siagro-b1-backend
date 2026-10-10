using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Web.Functions.Royalties;

/// <summary>Totais por situação dos pendentes, com os mesmos filtros da tela.</summary>
public class RoyaltyBalanceTotalsController(IUnitOfWork unitOfWork) : ODataController
{
    [HttpGet("odata/RoyaltyBalanceTotals(FromDate={fromDate},ToDate={toDate},CardCode={cardCode},OriginCode={originCode},ItemCode={itemCode},Situation={situation},StorageAddressCode={storageAddressCode})")]
    public async Task<IActionResult> Get(
        string? fromDate, string? toDate, string? cardCode, string? originCode,
        string? itemCode, string? situation, string? storageAddressCode)
    {
        var filter = new RoyaltyBalanceFilter
        {
            FromDate = ParseDate(fromDate),
            ToDate = ParseDate(toDate),
            CardCode = Clean(cardCode),
            OriginCode = Clean(originCode),
            ItemCode = Clean(itemCode),
            Situation = Enum.TryParse<RoyaltySituation>(Clean(situation), out var s) ? s : null,
            StorageAddressCode = Clean(storageAddressCode),
        };

        var totals = await RoyaltyBalanceQuery.TotalsAsync(
            RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(unitOfWork.Context), filter));
        return Ok(totals);
    }

    // Os parâmetros chegam como literal OData: 'texto' (com aspas) ou null.
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null") return null;
        var trimmed = value.Trim('\'');
        return trimmed.Length == 0 ? null : Uri.UnescapeDataString(trimmed).Replace("''", "'");
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(Clean(value), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d : null;
}
