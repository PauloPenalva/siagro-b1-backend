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

/// <summary>Regras de distribuição. Versões são mantidas pelas actions DistributionRulesSaveVersion/DeleteVersion.</summary>
public class DistributionRulesController(IDistributionRuleService service) : ODataController
{
    [EnableQuery]
    public virtual ActionResult Get()
    {
        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public virtual ActionResult Get([FromRoute] string key)
    {
        return Ok(SingleResult.Create(service.QueryAll().Where(x => x.Code == key)));
    }

    public virtual async Task<IActionResult> Post([FromBody] DistributionRule entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            return Created(await service.CreateAsync(entity));
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] string key, [FromBody] Delta<DistributionRule> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var current = service.QueryAll().FirstOrDefault(x => x.Code == key);

        if (current == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(current);

            return await service.UpdateHeaderAsync(key, current.Name, current.Inactive) == null
                ? NotFound()
                : NoContent();
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
