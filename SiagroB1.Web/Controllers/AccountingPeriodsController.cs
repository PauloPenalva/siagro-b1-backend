using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Calendário contábil — só leitura; gerar e mudar situação são actions. Só STANDALONE.</summary>
public class AccountingPeriodsController(IAccountingPeriodsService service, IConfiguration configuration)
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
