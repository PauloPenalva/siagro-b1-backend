using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Somente leitura: a entrada em armazenagem é criada e estornada exclusivamente
/// pelas actions StorageEntryTransactionsCreate/Cancel, que mantêm o par de
/// romaneios e os saldos consistentes.
/// </summary>
public class StorageEntryTransactionsController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<StorageEntryTransaction>> Get()
    {
        return Ok(unitOfWork.Context.StorageEntryTransactions.AsQueryable());
    }

    /// <summary>
    /// Sem esta sobrecarga o OData responde 404 em /StorageEntryTransactions(key)
    /// e a tela de detalhe abre sem dados.
    ///
    /// Devolve SingleResult (e não a entidade materializada) para que o EnableQuery
    /// consiga compor $expand/$select sobre o IQueryable — com um POCO já carregado
    /// as navegações voltariam nulas.
    /// </summary>
    [EnableQuery]
    public SingleResult<StorageEntryTransaction> Get([FromRoute] Guid key)
    {
        return SingleResult.Create(
            unitOfWork.Context.StorageEntryTransactions.Where(x => x.Key == key));
    }
}
