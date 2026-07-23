using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsPriceFixationCreateController(
    SalesContractsPriceFixationCreateService service) : ODataController
{
    [HttpPost("odata/SalesContractsPriceFixationCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("SalesContractKey", out var keyObj))
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Fixation", out var fixationObj)
                || fixationObj is not SalesContractPriceFixation fixation)
                return BadRequest("Missing fixation payload");

            var userName = User.Identity?.Name ?? "Unknown";
            var salesContractKey = (Guid) keyObj;

            await service.ExecuteAsync(salesContractKey, fixation, userName);

            // Ok() sem corpo, como as demais actions de fixação: o frontend recarrega a
            // list binding após invocar, e a linha nova aparece.
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
