using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// MDF-e (só STANDALONE). Leitura pelo entity set (com $expand dos filhos); inclusão a partir dos CT-e,
/// gravação do rascunho e exclusão pelas ações; CT-e elegíveis pela função.
/// </summary>
public class TransportManifestsController(
    MdfeEligibleCtesQuery eligibleQuery,
    TransportManifestsCreateFromCtesService createService,
    TransportManifestsSaveService saveService,
    IUnitOfWork db,
    IConfiguration configuration) : ODataController
{
    private const string InvalidKey = "Chave do MDF-e inválida.";

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchMdfeSettingsService.StandaloneOnly);
        return Ok(db.Context.TransportManifests.AsNoTracking());
    }

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchMdfeSettingsService.StandaloneOnly);
        return Ok(SingleResult.Create(db.Context.TransportManifests.AsNoTracking().Where(x => x.Key == key)));
    }

    [HttpGet("odata/MdfeEligibleCtes(BranchCode={branchCode})")]
    public async Task<IActionResult> EligibleCtesAsync([FromRoute] string branchCode)
    {
        try
        {
            return Ok(await eligibleQuery.ListAsync(branchCode.Trim('\'')));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportManifestsCreateFromCtes")]
    public async Task<IActionResult> CreateFromCtesAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, CteKeys.");

        var keys = new List<Guid>();
        if (parameters.TryGetValue("CteKeys", out var value) && value is IEnumerable sequence)
            foreach (var item in sequence.Cast<object?>())
            {
                if (!Guid.TryParse(item?.ToString(), out var k)) return BadRequest("Chave de CT-e inválida.");
                keys.Add(k);
            }

        try
        {
            var key = await createService.CreateFromCtesAsync(parameters["BranchCode"]?.ToString() ?? string.Empty, keys, UserName);
            return Ok(key.ToString());
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportManifestsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);
        if (parameters.GetValueOrDefault("Model") is not TransportManifestSaveModel model)
            return BadRequest("Parâmetro obrigatório ausente ou inválido: Model.");

        try
        {
            return Ok(await saveService.SaveAsync(key, model, UserName));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportManifestsDelete")]
    public async Task<IActionResult> DeleteAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);

        try
        {
            await saveService.DeleteAsync(key);
            return NoContent();
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private string UserName => User.Identity?.Name ?? "Unknown";
}
