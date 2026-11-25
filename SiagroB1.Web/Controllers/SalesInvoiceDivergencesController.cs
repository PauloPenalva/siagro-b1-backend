using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Divergências entre o efetivo da carga e a NF do SAP vinculada (GAC-1226): somente leitura. Quem escreve é o
/// serviço de vínculo, no mesmo SaveChanges do documento.
/// </summary>
public class SalesInvoiceDivergencesController(AppDbContext context) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceDivergence>> Get() =>
        Ok(context.SalesInvoiceDivergences.AsNoTracking());
}
