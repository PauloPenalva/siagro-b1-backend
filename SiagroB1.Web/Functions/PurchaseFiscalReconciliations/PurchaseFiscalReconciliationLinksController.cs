using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Vínculos de um romaneio — o diálogo de detalhe. Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationLinksController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationLinks(TransactionKey={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key) => Ok(await service.ListLinksAsync(key));
}
