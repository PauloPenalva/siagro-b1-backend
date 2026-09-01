using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Edição dos dados cadastrais da carga. Mesmas guardas de parâmetro da criação — ver o
/// <c>remarks</c> de <see cref="ShipmentLoadsCreateController"/>, cujos leitores são reusados.
/// </summary>
public class ShipmentLoadsUpdateController(
    ShipmentLoadsUpdateService updateService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsUpdate")]
    public async Task<ActionResult> Update(ODataActionParameters parameters)
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

            var input = new ShipmentLoad
            {
                Key = Guid.Parse(keyObj.ToString()!),
                BranchCode = ShipmentLoadsCreateController.Text(parameters, "BranchCode"),
                LoadDate = ShipmentLoadsCreateController.Date(parameters, "LoadDate") ?? DateTime.Now.Date,
                TruckCode = ShipmentLoadsCreateController.Text(parameters, "TruckCode"),
                TruckDriverCode = ShipmentLoadsCreateController.Text(parameters, "TruckDriverCode"),
                TruckDriverName = ShipmentLoadsCreateController.Text(parameters, "TruckDriverName"),
                CarrierCardCode = ShipmentLoadsCreateController.Text(parameters, "CarrierCardCode"),
                CarrierName = ShipmentLoadsCreateController.Text(parameters, "CarrierName"),
                ItemCode = ShipmentLoadsCreateController.Text(parameters, "ItemCode") ?? string.Empty,
                ItemName = ShipmentLoadsCreateController.Text(parameters, "ItemName"),
                UnitOfMeasureCode =
                    ShipmentLoadsCreateController.Text(parameters, "UnitOfMeasureCode") ?? string.Empty,
                WarehouseCode = ShipmentLoadsCreateController.Text(parameters, "WarehouseCode"),
                WarehouseName = ShipmentLoadsCreateController.Text(parameters, "WarehouseName"),
                CardCode = ShipmentLoadsCreateController.Text(parameters, "CardCode"),
                CardName = ShipmentLoadsCreateController.Text(parameters, "CardName"),
                HasExcess = ShipmentLoadsCreateController.Flag(parameters, "HasExcess"),
                FreightPrice = ShipmentLoadsCreateController.Money(parameters, "FreightPrice"),
                Comments = ShipmentLoadsCreateController.Text(parameters, "Comments"),
            };

            var userName = User.Identity?.Name ?? "Unknown";

            var load = await updateService.ExecuteAsync(input, userName);

            return Ok(new { load.Key, load.Code });
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
