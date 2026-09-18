using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>
/// Grid de anexos da carga (GAC-1171). Projeção SEM o binário — o arquivo só sai por
/// <c>ShipmentLoadsAttachmentsDownload</c>.
/// </summary>
public class ShipmentLoadsAttachmentsListController(
    ShipmentLoadAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/ShipmentLoadsAttachmentsList(LoadKey={key})")]
    public ActionResult List([FromRoute] Guid key) => Ok(service.ListByLoad(key));
}
