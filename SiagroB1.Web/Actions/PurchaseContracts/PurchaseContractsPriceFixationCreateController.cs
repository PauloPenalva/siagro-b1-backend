using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsPriceFixationCreateController(
    PurchaseContractsPriceFixationCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsPriceFixationCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("PurchaseContractKey", out var keyObj))
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Fixation", out var fixationObj)
                || fixationObj is not PurchaseContractPriceFixation fixation)
                return BadRequest("Missing fixation payload");

            var userName = User.Identity?.Name ?? "Unknown";
            var purchaseContractKey = (Guid) keyObj;

            await service.ExecuteAsync(purchaseContractKey, fixation, userName);

            // Ok() sem corpo, como as demais actions de fixação: o frontend recarrega a
            // list binding após invocar, e a linha nova aparece. Devolver a entidade
            // exigiria vinculá-la a um entity set no path da action (senão a serialização
            // do OData falha), complexidade que o refresh da lista dispensa.
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
