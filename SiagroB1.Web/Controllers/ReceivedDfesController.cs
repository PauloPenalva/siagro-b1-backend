using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// DF-e destinados à filial (somente leitura). O XML não sai do EDM: baixa-se pela function ReceivedDfesXml.
/// A tela filtra por filial com $filter, como as demais listas.
/// </summary>
[Route("odata/ReceivedDfes")]
public class ReceivedDfesController(ReceivedDfesGetService service) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<ReceivedDfe>> Get() => Ok(service.QueryAll());
}

/// <summary>Estado da consulta de DF-e por filial: próxima consulta permitida e horários agendados (somente leitura).</summary>
[Route("odata/BranchDfeSyncStates")]
public class BranchDfeSyncStatesController(ReceivedDfesGetService service) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<BranchDfeSyncState>> Get() => Ok(service.QuerySyncStates());
}
