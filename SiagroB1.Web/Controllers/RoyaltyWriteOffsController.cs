using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Baixas ITS — somente leitura; criar/cancelar só pelas actions.</summary>
public class RoyaltyWriteOffsController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 3)]
    public ActionResult<IEnumerable<RoyaltyWriteOff>> Get() =>
        Ok(unitOfWork.Context.RoyaltyWriteOffs.AsQueryable());

    // Sem este overload o OData devolve 404 em /RoyaltyWriteOffs(key); SingleResult para o $expand.
    [EnableQuery(MaxExpansionDepth = 3)]
    public SingleResult<RoyaltyWriteOff> Get([FromRoute] Guid key) =>
        SingleResult.Create(unitOfWork.Context.RoyaltyWriteOffs.Where(x => x.Key == key));
}
