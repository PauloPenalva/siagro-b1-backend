using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class ShipmentLoadsController(ShipmentLoadsGetService getService) : ODataController
{
    [HttpGet("odata/ShipmentLoads")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoad>> Get()
    {
        return Ok(getService.QueryAll());
    }

    // As duas formas de rota: o UI5 pede ora ShipmentLoads(guid), ora ShipmentLoads/guid.
    [HttpGet("odata/ShipmentLoads({key:guid})")]
    [HttpGet("odata/ShipmentLoads/{key:guid}")]
    [EnableQuery]
    public async Task<ActionResult<ShipmentLoad>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    // Rotas de navegação declaradas à mão: sem elas o UI5 toma 404 ao expandir a coleção
    // pela URL do pai.
    [HttpGet("odata/ShipmentLoads({key:guid})/Transactions")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Transactions")]
    [EnableQuery]
    public ActionResult<IEnumerable<StorageTransaction>> GetTransactions([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll()
            .Where(x => x.Key == key)
            .SelectMany(x => x.Transactions));
    }

    [HttpGet("odata/ShipmentLoads({key:guid})/Invoices")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Invoices")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoice>> GetInvoices([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll()
            .Where(x => x.Key == key)
            .SelectMany(x => x.Invoices));
    }

    [HttpGet("odata/ShipmentLoads({key:guid})/Movements")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Movements")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadMovement>> GetMovements([FromRoute] Guid key)
    {
        return Ok(getService.QueryMovements().Where(x => x.ShipmentLoadKey == key));
    }
}
