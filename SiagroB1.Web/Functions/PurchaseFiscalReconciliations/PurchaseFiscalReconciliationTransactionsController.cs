using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Romaneios do recorte da Conciliação Fiscal de Compras (spec 2026-10-13 §6.1). Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationTransactionsController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationTransactions(CardCode={cardCode},ItemCode={itemCode},ContractCode={contractCode},DateFrom={dateFrom},DateTo={dateTo})")]
    public async Task<IActionResult> Get(
        [FromRoute] string? cardCode, [FromRoute] string? itemCode, [FromRoute] string? contractCode,
        [FromRoute] string? dateFrom, [FromRoute] string? dateTo)
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter(cardCode, itemCode, contractCode, dateFrom, dateTo, null, false);
        if (filter is null) return BadRequest(error);

        try
        {
            return Ok(await service.ListTransactionsAsync(filter));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
