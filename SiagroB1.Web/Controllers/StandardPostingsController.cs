using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Lançamento padrão (CT5): consulta com as sequências e gravação por código (POST substitui as sequências).
/// O catálogo de códigos e fontes vem da função StandardPostingCatalogGet. Só STANDALONE.
/// </summary>
public class StandardPostingsController(IStandardPostingsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(service.QueryAll());
    }

    public async Task<IActionResult> Post([FromBody] StandardPosting entity)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (entity is null) return BadRequest("Informe o lançamento padrão.");

        try { return Created(await service.SaveAsync(entity, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpGet("odata/StandardPostingCatalogGet")]
    public ActionResult Catalog()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(StandardPostingCatalog.All);
    }
}
