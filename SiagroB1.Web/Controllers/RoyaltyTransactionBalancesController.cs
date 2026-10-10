using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Saldo de royalties (ITS) por romaneio de recebimento e de compra (carregamento em terceiro) —
/// com e sem saldo; a aba Pendentes
/// filtra <c>Balance gt 0</c>. Somente leitura.
/// </summary>
public class RoyaltyTransactionBalancesController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxTop = 5000)]
    public ActionResult<IEnumerable<RoyaltyBalanceRow>> Get() =>
        Ok(RoyaltyBalanceQuery.Build(unitOfWork.Context));

    [EnableQuery]
    public SingleResult<RoyaltyBalanceRow> Get([FromRoute] Guid key) =>
        SingleResult.Create(RoyaltyBalanceQuery.Build(unitOfWork.Context).Where(x => x.StorageTransactionKey == key));
}
