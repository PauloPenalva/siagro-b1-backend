using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Linhas de baixa ITS — consultadas pela seção "Baixas ITS" do romaneio.</summary>
public class RoyaltyWriteOffLinesController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult<IEnumerable<RoyaltyWriteOffLine>> Get() =>
        Ok(unitOfWork.Context.RoyaltyWriteOffLines.AsQueryable());
}
