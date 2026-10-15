using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Leitura do washout de venda. Toda mutação é OData action (SalesContractsWashout*). As rotas são declaradas à mão:
/// navegação e GET por chave dão 404 sem isso.
/// </summary>
public class SalesContractsWashoutsController(SalesContractsWashoutsGetService getService) : ODataController
{
    [HttpGet("odata/SalesContracts({key:guid})/Washouts")]
    [HttpGet("odata/SalesContracts/{key:guid}/Washouts")]
    [EnableQuery]
    public ActionResult<IQueryable<SalesContractWashout>> GetWashouts([FromRoute] Guid key) =>
        Ok(getService.QueryByContract(key));

    /// <summary>Fila da tela "Aprovação de Washouts" do grupo Vendas.</summary>
    [HttpGet("odata/SalesContractsWashouts")]
    [EnableQuery]
    public ActionResult<IQueryable<SalesContractWashout>> GetPending() =>
        Ok(getService.QueryPending());

    /// <summary>SingleResult sobre IQueryable: materializado, o $expand seria aceito e voltaria nulo.</summary>
    [HttpGet("odata/SalesContractsWashouts({key:guid})")]
    [HttpGet("odata/SalesContractsWashouts/{key:guid}")]
    [EnableQuery]
    public SingleResult<SalesContractWashout> GetByKey([FromRoute] Guid key) =>
        SingleResult.Create(getService.QueryByKey(key));
}
