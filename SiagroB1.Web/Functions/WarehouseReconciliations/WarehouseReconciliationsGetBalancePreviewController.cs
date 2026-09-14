using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsGetBalancePreviewController(
    WarehouseReconciliationsGetBalancePreviewService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsGetBalancePreview(WarehouseCode={warehouseCode},ItemCode={itemCode},ReferenceDate={referenceDate})")]
    public async Task<ActionResult> Get([FromRoute] string warehouseCode, [FromRoute] string itemCode, [FromRoute] string referenceDate)
    {
        // A data vai como string 'yyyy-MM-dd': Date/DateTimeOffset em URL de função é frágil no UI5 v4.
        if (!DateTime.TryParseExact(referenceDate.Trim('\''), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return BadRequest("ReferenceDate deve estar no formato yyyy-MM-dd.");

        return Ok(await service.ExecuteAsync(warehouseCode.Trim('\''), itemCode.Trim('\''), date));
    }
}
