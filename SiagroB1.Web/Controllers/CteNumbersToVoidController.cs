using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Lista de números do CT-e descartados/pendentes de inutilização. Só leitura (só STANDALONE).</summary>
public class CteNumbersToVoidController(IUnitOfWork db, IConfiguration configuration) : ODataController
{
    [EnableQuery]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);

        return Ok(db.Context.CteNumbersToVoid.AsNoTracking());
    }

    [EnableQuery]
    public IActionResult Get([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);

        return Ok(SingleResult.Create(db.Context.CteNumbersToVoid.AsNoTracking().Where(x => x.Key == key)));
    }
}
