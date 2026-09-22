using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsAttachTransactionsController(
    ShipmentLoadsAttachTransactionsService attachService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachTransactions")]
    public async Task<ActionResult> Attach(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("StorageTransactionKeys", out var keysObj) ||
                keysObj is not IEnumerable<Guid> keys)
            {
                return BadRequest("Selecione ao menos um romaneio de embarque para vincular.");
            }

            var key = Guid.Parse(keyObj.ToString()!);
            var userName = User.Identity?.Name ?? "Unknown";

            // GAC-1181: presente, a saída vinculada assume o papel de saída do transbordo — ver
            // ShipmentLoadsAttachTransactionsService. Ausente (leitura defensiva: TryGetValue
            // devolve true com valor nulo quando o parâmetro opcional simplesmente não veio),
            // segue o caminho comum.
            parameters.TryGetValue("TransshipmentKey", out var transshipmentKeyObj);
            var transshipmentKey = transshipmentKeyObj as Guid?;

            var load = await attachService.ExecuteAsync(key, keys.ToList(), transshipmentKey, userName);

            return Ok(new { load.Key, load.Code, load.TotalQuantity, load.Status });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }

            return BadRequest(e.Message);
        }
    }
}
