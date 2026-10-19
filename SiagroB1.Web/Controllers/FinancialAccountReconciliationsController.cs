using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.AspNetCore.OData.Results;
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

    /// <summary>SingleResult (consulta, não entidade materializada) para o $expand=FinancialAccount rodar no SQL.</summary>
    [EnableQuery]
    public SingleResult<FinancialAccountReconciliation> Get([FromRoute] Guid key) =>
        SingleResult.Create(db.Context.FinancialAccountReconciliations.AsNoTracking().Where(x => x.Key == key));
}
