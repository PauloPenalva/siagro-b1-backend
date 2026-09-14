using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Leitura do washout. Toda mutação é OData action (PurchaseContractsWashout*). As rotas são
/// declaradas à mão: navegação e GET por chave dão 404 sem isso.
/// </summary>
public class PurchaseContractsWashoutsController(PurchaseContractsWashoutsGetService getService) : ODataController
{
    [HttpGet("odata/PurchaseContracts({key:guid})/Washouts")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Washouts")]
    [EnableQuery]
    public ActionResult<IQueryable<PurchaseContractWashout>> GetWashouts([FromRoute] Guid key) =>
        Ok(getService.QueryByContract(key));

    /// <summary>Fila da tela "Aprovação de Washouts".</summary>
    [HttpGet("odata/PurchaseContractsWashouts")]
    [EnableQuery]
    public ActionResult<IQueryable<PurchaseContractWashout>> GetPending() =>
        Ok(getService.QueryPending());

    /// <summary>
    /// SingleResult sobre IQueryable: materializado, o $expand seria aceito e voltaria nulo
    /// (bug do Motivo em branco da Conferência, 14/09/2026).
    /// </summary>
    [HttpGet("odata/PurchaseContractsWashouts({key:guid})")]
    [HttpGet("odata/PurchaseContractsWashouts/{key:guid}")]
    [EnableQuery]
    public SingleResult<PurchaseContractWashout> GetByKey([FromRoute] Guid key) =>
        SingleResult.Create(getService.QueryByKey(key));
}
