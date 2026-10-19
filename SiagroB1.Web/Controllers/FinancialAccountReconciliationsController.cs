using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Leitura das reconciliações; toda mudança passa pelas ações FinancialAccountReconciliations*.</summary>
public class FinancialAccountReconciliationsController(IUnitOfWork db) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialAccountReconciliation>> Get() =>
        Ok(db.Context.FinancialAccountReconciliations.AsNoTracking());

    [EnableQuery]
    public async Task<ActionResult<FinancialAccountReconciliation>> Get([FromRoute] Guid key)
    {
        var entity = await db.Context.FinancialAccountReconciliations.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return entity is null ? NotFound() : Ok(entity);
    }
}
