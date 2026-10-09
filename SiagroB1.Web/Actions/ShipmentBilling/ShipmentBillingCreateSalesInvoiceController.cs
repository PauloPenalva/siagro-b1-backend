using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentBilling;

public class ShipmentBillingCreateSalesInvoiceController(
    ShipmentBillingCreateSalesInvoiceService service
    )
    : ODataController
{

    [HttpPost("odata/ShipmentBillingCreateSalesInvoice")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("SalesInvoice", out var salesInvoiceObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var salesInvoice = (SalesInvoice) salesInvoiceObj;
            
            await service.ExecuteAsync(salesInvoice, userName);

            // A chave do documento criado: o detalhe da carga leva o usuário direto a ele, onde está o
            // "Transmitir NF-e". camelCase, como as demais respostas Ok(new { … }) deste projeto.
            return Ok(new { key = salesInvoice.Key, invoiceNumber = salesInvoice.InvoiceNumber });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is DefaultException or ApplicationException)
            {
                return BadRequest(e.Message);
            }
            
            return StatusCode(500, e.Message);
        }
    }
}