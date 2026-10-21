using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Controllers;

/// <summary>Dimensões gerenciais: as 5 linhas são fixas, então só há GET e PATCH.</summary>
public class AccountingDimensionsController(IAccountingDimensionService service) : ODataController
{
    [EnableQuery]
    public virtual ActionResult Get()
    {
        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public virtual ActionResult Get([FromRoute] int key)
    {
        return Ok(SingleResult.Create(service.QueryAll().Where(x => x.Number == key)));
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<AccountingDimension> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var current = service.QueryAll().FirstOrDefault(x => x.Number == key);

        if (current == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(current);

            var updated = await service.UpdateAsync(key, current);

            if (updated == null)
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}
