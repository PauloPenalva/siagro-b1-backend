using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Controllers;

/// <summary>Só leitura: toda mutação de minuta passa pelas actions (guardas de status).</summary>
public class ContractDraftsController(AppDbContext context) : ODataController
{
    [EnableQuery]
    public ActionResult<IQueryable<ContractDraft>> Get() => Ok(context.ContractDrafts.AsNoTracking());
}
