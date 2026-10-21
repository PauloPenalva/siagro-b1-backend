using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Meses do estoque fiscal (fechados/reabertos) — só leitura; fechar/reabrir são actions. Só STANDALONE.</summary>
public class FiscalStockPeriodsController(IFiscalStockPeriodsService service, IConfiguration configuration)
    : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [EnableQuery]
    public ActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);

        return Ok(service.QueryAll());
    }
}
