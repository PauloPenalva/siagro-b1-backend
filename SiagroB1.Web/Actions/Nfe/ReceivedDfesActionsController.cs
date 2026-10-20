using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// Manifestação do destinatário (DF-e): consulta à SEFAZ, agenda, manifestação e XML. Tudo action/function de
/// topo — navegação e late property dão 404 neste projeto. Parâmetro de string do OData é anulável, e o corpo
/// pode chegar sem parâmetros.
/// </summary>
public class ReceivedDfesActionsController(
    ReceivedDfeSyncService sync,
    ReceivedDfeManifestService manifest,
    ReceivedDfeXmlService xml) : ODataController
{
    [HttpPost("odata/ReceivedDfesSync")]
    public async Task<IActionResult> SyncAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (Text(parameters, "BranchCode") is not { Length: > 0 } branchCode)
            return BadRequest("Informe a filial.");

        try { return Ok(await sync.SyncBranchAsync(branchCode, ct)); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (NfeCommunicationException e) { return BadRequest($"Sem resposta da SEFAZ: {e.Message}"); }
    }

    [HttpPost("odata/BranchDfeSyncStatesSetSchedule")]
    public async Task<IActionResult> SetScheduleAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (Text(parameters, "BranchCode") is not { Length: > 0 } branchCode)
            return BadRequest("Informe a filial.");

        // Lista vazia é legítima: desliga a consulta automática.
        var times = parameters!.TryGetValue("Times", out var value) && value is IEnumerable<string> list
            ? list.Where(t => t is not null).ToList()
            : [];

        try { return Ok(await sync.SetScheduleAsync(branchCode, times, ct)); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/ReceivedDfesManifest")]
    public async Task<IActionResult> ManifestAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (parameters is null || !parameters.TryGetValue("Id", out var idValue) || idValue is null ||
            !int.TryParse(idValue.ToString(), out var id))
            return BadRequest("Informe o documento.");

        if (!Enum.TryParse<NfeManifestationType>(Text(parameters, "Type"), ignoreCase: true, out var type) ||
            !Enum.IsDefined(type))
            return BadRequest("Tipo de manifestação inválido.");

        try { return Ok(await manifest.ExecuteAsync(id, type, Text(parameters, "Justification"), ct)); }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (NfeCommunicationException e) { return BadRequest($"Sem resposta da SEFAZ: {e.Message}"); }
    }

    [HttpGet("odata/ReceivedDfesXml(Id={id})")]
    public async Task<IActionResult> XmlAsync([FromRoute] int id, CancellationToken ct)
    {
        try
        {
            var (content, fileName) = await xml.GetAsync(id, ct);
            return Ok(new ReceivedDfeXmlDto(System.Text.Encoding.UTF8.GetString(content), fileName));
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (NfeCommunicationException e) { return BadRequest($"Sem resposta da SEFAZ: {e.Message}"); }
    }

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
