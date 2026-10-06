using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsShipController(ShipmentLoadsShipService shipService) : ODataController
{
    [HttpPost("odata/ShipmentLoadsShip")]
    public async Task<ActionResult> Ship(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj == null ||
                !parameters.TryGetValue("ShipmentReleaseKey", out var releaseObj) || releaseObj == null)
                return BadRequest("Selecione a liberação de embarque.");

            parameters.TryGetValue("WarehouseCode", out var warehouseObj);
            parameters.TryGetValue("TruckDriverCode", out var driverObj);
            parameters.TryGetValue("TransactionDate", out var dateObj);
            parameters.TryGetValue("GrossWeight", out var weightObj);
            parameters.TryGetValue("Comments", out var commentsObj);

            var warehouse = warehouseObj as string;
            var driver = driverObj as string;
            if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(driver))
                return BadRequest("Informe o armazém e o motorista.");

            var date = dateObj is DateTimeOffset dto ? dto.DateTime : DateTime.Now.Date;
            var weight = weightObj is double d ? (decimal)d : decimal.Zero;

            var result = await shipService.ExecuteAsync(
                new ShipmentLoadShipRequest(
                    Guid.Parse(keyObj.ToString()!), Guid.Parse(releaseObj.ToString()!), warehouse, driver, date, weight,
                    commentsObj as string),
                User.Identity?.Name ?? "Unknown");

            return Ok(new
            {
                shipmentLoadKey = result.ShipmentLoadKey,
                storageTransactionKey = result.StorageTransactionKey,
                storageTransactionCode = result.StorageTransactionCode,
            });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound();

            // A carga tem RowVersion: um faturamento/expedição concorrente derruba esta chamada.
            // O serviço relança como veio, mas o EF pode chegar embrulhado em InnerException.
            for (var inner = e; inner != null; inner = inner.InnerException)
                if (inner is DbUpdateConcurrencyException)
                    return BadRequest("A carga foi alterada por outro usuário. Atualize e tente novamente.");

            return BadRequest(e.Message);
        }
    }
}
