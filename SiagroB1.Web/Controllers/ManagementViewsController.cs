using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Visão gerencial (CTS): consulta com as linhas e gravação por código (POST substitui as linhas). Só STANDALONE.</summary>
public class ManagementViewsController(IManagementViewsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(service.QueryAll());
    }

    public async Task<IActionResult> Post([FromBody] ManagementView entity)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (entity is null) return BadRequest("Informe a visão gerencial.");

        try { return Created(await service.SaveAsync(entity, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }
}
