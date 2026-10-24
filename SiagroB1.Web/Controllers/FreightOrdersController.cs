using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Ordem de frete (só STANDALONE). Leitura pelo entity set; gravação, encerramento, cancelamento e geração do CT-e pelas ações.</summary>
public class FreightOrdersController(
    FreightOrdersSaveService saveService,
    FreightOrdersCloseService closeService,
    FreightOrdersCancelService cancelService,
    FreightOrdersCreateCteService createCteService,
    IUnitOfWork db,
    IConfiguration configuration) : ODataController
{
    private const string InvalidKey = "Chave da ordem de frete inválida.";

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        return Ok(db.Context.FreightOrders.AsNoTracking());
    }

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        return Ok(SingleResult.Create(db.Context.FreightOrders.AsNoTracking().Where(x => x.Key == key)));
    }

    [HttpPost("odata/FreightOrdersSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        if (parameters?.GetValueOrDefault("Model") is not FreightOrderSaveModel model)
            return BadRequest("Parâmetro obrigatório ausente ou inválido: Model.");

        try
        {
            return Ok(await saveService.SaveAsync(model, UserName));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/FreightOrdersClose")]
    public Task<IActionResult> CloseAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, async key =>
        {
            await closeService.CloseAsync(key, UserName);
            return NoContent();
        });

    [HttpPost("odata/FreightOrdersCancel")]
    public Task<IActionResult> CancelAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, async key =>
        {
            await cancelService.CancelAsync(key, UserName);
            return NoContent();
        });

    [HttpPost("odata/FreightOrdersCreateCte")]
    public Task<IActionResult> CreateCteAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, async key => Ok((await createCteService.CreateDraftAsync(key, UserName)).ToString()));

    private async Task<IActionResult> RunAsync(ODataActionParameters? parameters, Func<Guid, Task<IActionResult>> action)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);

        try
        {
            return await action(key);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private string UserName => User.Identity?.Name ?? "Unknown";
}
