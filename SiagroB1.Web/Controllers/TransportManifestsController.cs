using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Nfe;
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
    TransportManifestsConfirmService confirmService,
    TransportManifestsReopenService reopenService,
    TransportManifestsDiscardNumberService discardService,
    TransportManifestsTransmitService transmitService,
    TransportManifestsConsultService consultService,
    TransportManifestsCloseService closeService,
    TransportManifestsCloseBatchService closeBatchService,
    MdfeXmlZipService xmlZipService,
    TransportManifestsIncludeDriverService includeDriverService,
    TransportManifestsCancelService cancelService,
    MdfeNotClosedQueryService notClosedQuery,
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

    [HttpGet("odata/MdfeNotClosed(BranchCode={branchCode})")]
    public async Task<IActionResult> NotClosedAsync([FromRoute] string branchCode)
    {
        try
        {
            return Ok(await notClosedQuery.ListAsync(branchCode.Trim('\'')));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
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

    [HttpPost("odata/TransportManifestsConfirm")]
    public Task<IActionResult> ConfirmAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, key => confirmService.ConfirmAsync(key, UserName));

    [HttpPost("odata/TransportManifestsReopen")]
    public Task<IActionResult> ReopenAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, reopenService.ReopenAsync);

    [HttpPost("odata/TransportManifestsDiscardNumber")]
    public Task<IActionResult> DiscardNumberAsync([FromBody] ODataActionParameters parameters) =>
        RunAsync(parameters, discardService.DiscardNumberAsync);

    [HttpPost("odata/TransportManifestsTransmit")]
    public Task<IActionResult> TransmitAsync([FromBody] ODataActionParameters parameters) =>
        RunSefazAsync(parameters, key => transmitService.TransmitAsync(key, UserName));

    [HttpPost("odata/TransportManifestsConsult")]
    public Task<IActionResult> ConsultAsync([FromBody] ODataActionParameters parameters) =>
        RunSefazAsync(parameters, consultService.ConsultAsync);

    [HttpPost("odata/TransportManifestsClose")]
    public Task<IActionResult> CloseAsync([FromBody] ODataActionParameters parameters)
    {
        DateOnly? closedOn;
        try
        {
            closedOn = TransportManifestsCloseService.ParseClosedOn(parameters?.GetValueOrDefault("ClosedOn")?.ToString());
        }
        catch (DefaultException e)
        {
            return Task.FromResult<IActionResult>(BadRequest(e.Message));
        }

        var ufCode = parameters?.GetValueOrDefault("UfCode")?.ToString();
        var cityIbge = parameters?.GetValueOrDefault("CityIbge")?.ToString();
        return RunSefazAsync(parameters, async key =>
        {
            var r = await closeService.CloseAsync(key, closedOn, ufCode, cityIbge, UserName);
            return new NfeSefazResult(r.StatusCode, r.Reason);
        });
    }

    /// <summary>Encerra vários MDF-e; devolve {Key, Number, Success, Message} por item (falha de item não é 400).</summary>
    [HttpPost("odata/TransportManifestsCloseBatch")]
    public async Task<IActionResult> CloseBatchAsync([FromBody] ODataActionParameters parameters)
    {
        var keys = new List<Guid>();
        if (parameters is not null && parameters.TryGetValue("Keys", out var value) && value is IEnumerable sequence)
            foreach (var item in sequence.Cast<object?>())
            {
                if (!Guid.TryParse(item?.ToString(), out var k)) return BadRequest(InvalidKey);
                keys.Add(k);
            }
        if (keys.Count == 0) return BadRequest("Informe ao menos um MDF-e.");
        if (keys.Count > TransportManifestsCloseBatchService.MaxKeys) return BadRequest("Selecione no máximo 50 MDF-e por vez.");

        try
        {
            var closedOn = TransportManifestsCloseService.ParseClosedOn(parameters!.GetValueOrDefault("ClosedOn")?.ToString());
            return Ok(await closeBatchService.CloseBatchAsync(keys, closedOn, UserName));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>ZIP com o XML dos MDF-e autorizados (inclusive encerrados/cancelados) e seus eventos; máx. 50.</summary>
    [HttpPost("odata/TransportManifestsXmlZip")]
    public async Task<IActionResult> XmlZipAsync([FromBody] ODataActionParameters parameters)
    {
        var keys = new List<Guid>();
        if (parameters is not null && parameters.TryGetValue("Keys", out var value) && value is IEnumerable sequence)
            foreach (var item in sequence.Cast<object?>())
            {
                if (!Guid.TryParse(item?.ToString(), out var k)) return BadRequest(InvalidKey);
                keys.Add(k);
            }
        if (keys.Count == 0) return BadRequest("Informe ao menos um MDF-e.");

        try
        {
            return File(await xmlZipService.BuildAsync(keys), "application/zip", "xml-mdfe.zip");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportManifestsCancel")]
    public Task<IActionResult> CancelAsync([FromBody] ODataActionParameters parameters)
    {
        var justification = parameters?.GetValueOrDefault("Justification")?.ToString() ?? string.Empty;
        return RunSefazAsync(parameters, async key =>
        {
            var r = await cancelService.CancelAsync(key, justification, UserName);
            return new NfeSefazResult(r.StatusCode, r.Reason);
        });
    }

    [HttpPost("odata/TransportManifestsIncludeDriver")]
    public Task<IActionResult> IncludeDriverAsync([FromBody] ODataActionParameters parameters)
    {
        var cpf = parameters?.GetValueOrDefault("Cpf")?.ToString();
        var name = parameters?.GetValueOrDefault("Name")?.ToString();
        return RunSefazAsync(parameters, async key =>
        {
            var r = await includeDriverService.IncludeDriverAsync(key, cpf, name, UserName);
            return new NfeSefazResult(r.StatusCode, r.Reason);
        });
    }

    /// <summary>mdfeProc autorizado (MDF-e assinado + protMDFe) para download.</summary>
    [HttpGet("odata/TransportManifestsXml(Key={key})")]
    public async Task<IActionResult> XmlAsync([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchMdfeSettingsService.StandaloneOnly);
        var m = await db.Context.TransportManifests.AsNoTracking().Where(x => x.Key == key)
            .Select(x => new { x.AccessKey, Xml = x.Xmls.Where(v => v.Kind == MdfeXmlKind.Authorized).Select(v => v.Xml).FirstOrDefault() })
            .FirstOrDefaultAsync();
        if (m?.Xml is null) return NotFound("O MDF-e não tem XML autorizado.");

        return File(System.Text.Encoding.UTF8.GetBytes(m.Xml), "application/xml", $"{m.AccessKey}-mdfe.xml");
    }

    private async Task<IActionResult> RunSefazAsync(ODataActionParameters parameters, Func<Guid, Task<NfeSefazResult>> action)
    {
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);

        try
        {
            var result = await action(key);
            return Ok(new NfeServiceStatusDto { StatusCode = result.StatusCode, Reason = result.Reason });
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (NfeCommunicationException e)
        {
            return BadRequest($"Sem resposta da SEFAZ: {e.Message}");
        }
    }

    private async Task<IActionResult> RunAsync(ODataActionParameters parameters, Func<Guid, Task> action)
    {
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);

        try
        {
            await action(key);
            return NoContent();
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private string UserName => User.Identity?.Name ?? "Unknown";
}
