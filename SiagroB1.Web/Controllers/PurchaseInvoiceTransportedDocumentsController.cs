using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// NF-e transportadas pelo CT-e: conjunto de topo somente leitura (a rota de navegação PurchaseInvoices(key)/X dá 404
/// neste projeto). Filtre por PurchaseInvoiceKey e use $expand=SalesInvoice,TransportedPurchaseInvoice. A escrita passa
/// pelas actions PurchaseInvoicesTransportedDocument{Add,Remove}/Relink.
/// </summary>
public class PurchaseInvoiceTransportedDocumentsController(AppDbContext context) : ODataController
{
    [HttpGet("odata/PurchaseInvoiceTransportedDocuments")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoiceTransportedDocument>> Get()
    {
        return Ok(context.PurchaseInvoiceTransportedDocuments.AsQueryable());
    }
}
