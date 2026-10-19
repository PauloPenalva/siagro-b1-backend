using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Histórico de e-mails da NF-e: somente leitura (as linhas nascem no fluxo fiscal e no reenvio manual).
/// Filtre por DocumentType/DocumentKey e ordene por CreatedAt desc.
/// </summary>
public class NfeEmailMessagesController(NfeEmailMessagesGetService getService, IConfiguration configuration) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 1)]
    public ActionResult<IEnumerable<NfeEmailMessage>> Get() =>
        SiagroB1.Infra.ErpMode.IsStandalone(configuration)
            ? Ok(getService.QueryAll())
            : Ok(Array.Empty<NfeEmailMessage>().AsQueryable());
}
