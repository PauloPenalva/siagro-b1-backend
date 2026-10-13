using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Linhas de NF do recorte, com Disponível em KG e o aviso de emitente (D6). Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationInvoiceLinesController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationInvoiceLines(CardCode={cardCode},ItemCode={itemCode},ContractCode={contractCode},DateFrom={dateFrom},DateTo={dateTo},IssuerCardCode={issuerCardCode},IncludeFullyLinked={includeFullyLinked})")]
    public async Task<IActionResult> Get(
        [FromRoute] string? cardCode, [FromRoute] string? itemCode, [FromRoute] string? contractCode,
        [FromRoute] string? dateFrom, [FromRoute] string? dateTo, [FromRoute] string? issuerCardCode,
        [FromRoute] bool includeFullyLinked)
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter(
            cardCode, itemCode, contractCode, dateFrom, dateTo, issuerCardCode, includeFullyLinked);
        if (filter is null) return BadRequest(error);

        try
        {
            return Ok(await service.ListInvoiceLinesAsync(filter));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
