using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Conjuntos de veículos (só STANDALONE). Leitura e exclusão pelo entity set; inclusão e alteração
/// pela ação TruckCombinationsSave, porque o PATCH não substitui a lista de reboques.
/// </summary>
public class TruckCombinationsController(TruckCombinationService service, IUnitOfWork db, IConfiguration configuration) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(TruckCombinationService.StandaloneOnly);

        return Ok(db.Context.TruckCombinations.AsNoTracking());
    }

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get([FromRoute] string key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(TruckCombinationService.StandaloneOnly);

        return Ok(SingleResult.Create(db.Context.TruckCombinations.AsNoTracking().Where(x => x.Code == key)));
    }

    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        try
        {
            await service.DeleteAsync(key);
            return NoContent();
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TruckCombinationsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("Code"))
            return BadRequest("Parâmetros obrigatórios: Code, Description, TractionTruckCode, DefaultDriverCode, Inactive, IsNew, TrailerCodes.");

        var trailers = parameters.TryGetValue("TrailerCodes", out var value) && value is IEnumerable sequence
            ? sequence.Cast<object?>().Select(x => x?.ToString() ?? string.Empty).Where(x => x.Length > 0).ToList()
            : [];

        var input = new TruckCombinationInput(
            Text(parameters, "Code") ?? string.Empty,
            Text(parameters, "Description") ?? string.Empty,
            Text(parameters, "TractionTruckCode") ?? string.Empty,
            Text(parameters, "DefaultDriverCode"),
            parameters.TryGetValue("Inactive", out var inactive) && inactive is not null && Convert.ToBoolean(inactive),
            trailers);

        try
        {
            var isNew = parameters.TryGetValue("IsNew", out var flag) && flag is not null && Convert.ToBoolean(flag);
            var saved = await service.SaveAsync(input, isNew);
            return Ok(saved.Code);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString() : null;
}
