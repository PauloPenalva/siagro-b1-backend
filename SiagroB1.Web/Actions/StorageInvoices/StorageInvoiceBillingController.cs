using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageInvoices;

/// <summary>Faturamento da fatura de armazenagem (spec 2026-10-19 parte 3): documentos, títulos e saldo para a tela.</summary>
public class StorageInvoiceBillingController(StorageInvoicesBillingGetService service) : ODataController
{
    [HttpGet("odata/StorageInvoiceBilling(Key={key})")]
    public async Task<IActionResult> GetAsync([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
