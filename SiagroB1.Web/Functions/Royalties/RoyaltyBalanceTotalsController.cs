using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Web.Functions.Royalties;

/// <summary>Totais por situação (abrangência: a baixar, baixados ou todos), com os mesmos filtros da tela.</summary>
public class RoyaltyBalanceTotalsController(IUnitOfWork unitOfWork) : ODataController
{
    [HttpGet("odata/RoyaltyBalanceTotals(FromDate={fromDate},ToDate={toDate},CardCode={cardCode},OriginCode={originCode},ItemCode={itemCode},Situation={situation},StorageAddressCode={storageAddressCode},Scope={scope})")]
    public async Task<IActionResult> Get(
        [FromODataUri] string? fromDate, [FromODataUri] string? toDate, [FromODataUri] string? cardCode,
        [FromODataUri] string? originCode, [FromODataUri] string? itemCode, [FromODataUri] string? situation,
        [FromODataUri] string? storageAddressCode, [FromODataUri] string? scope)
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
            Scope = Enum.TryParse<RoyaltyBalanceScope>(Clean(scope), out var sc) ? sc : RoyaltyBalanceScope.Pending,
        };

        var totals = await RoyaltyBalanceQuery.TotalsAsync(
            RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(unitOfWork.Context), filter));
        return Ok(totals);
    }

    // [FromODataUri] é obrigatório: sem ele o MVC liga o route value cru, e o literal `null` chega
    // como a INSTÂNCIA ODataNullValue — cujo ToString() é "Microsoft.OData.ODataNullValue". Esse
    // texto virava filtro (CardCode = "Microsoft.OData.ODataNullValue") e os totais vinham vazios.
    // Com ele, `null` chega null e 'texto' chega já sem aspas e decodificado.
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(Clean(value), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d : null;
}
