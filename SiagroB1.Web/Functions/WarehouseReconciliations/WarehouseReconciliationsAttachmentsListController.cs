using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentsListController(
    WarehouseReconciliationAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsAttachmentsList(ReconciliationKey={key})")]
    public ActionResult List([FromRoute] Guid key) => Ok(service.ListByReconciliation(key));
}
