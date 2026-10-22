using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Configuração de transporte da filial (seção "Transportadora" da tela da filial, só STANDALONE).
/// Mesmo estilo de BranchNfeSettings: função de leitura + ações de gravar e testar. A senha da AT&amp;M nunca sai.
/// </summary>
public class BranchTransportSettingsController(BranchTransportSettingsService service) : ODataController
{
    [HttpGet("odata/BranchTransportSettingsGet(BranchCode={branchCode})")]
    public async Task<IActionResult> GetAsync([FromRoute] string branchCode)
    {
        try
        {
            return Ok(await service.GetAsync(branchCode.Trim('\'')));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/BranchTransportSettingsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, CompanyRntrc, InsuranceResponsible, InsurerTaxId, InsurerName, " +
                              "PolicyNumber, AverbaEnabled, AverbaUser, AverbaPassword, AverbaCode, AverbaEnvironment.");

        var responsible = Number(parameters, "InsuranceResponsible");
        var model = new BranchTransportSettingsModel
        {
            CompanyRntrc = Text(parameters, "CompanyRntrc"),
            InsuranceResponsible = responsible is null or 0 ? null : (InsuranceResponsible)responsible.Value,
            InsurerTaxId = Text(parameters, "InsurerTaxId"),
            InsurerName = Text(parameters, "InsurerName"),
            PolicyNumber = Text(parameters, "PolicyNumber"),
            AverbaEnabled = parameters.TryGetValue("AverbaEnabled", out var enabled) && enabled is not null && Convert.ToBoolean(enabled),
            AverbaUser = Text(parameters, "AverbaUser"),
            AverbaPassword = Text(parameters, "AverbaPassword"),
            AverbaCode = Text(parameters, "AverbaCode"),
            AverbaEnvironment = (NfeEnvironment)(Number(parameters, "AverbaEnvironment") ?? (int)NfeEnvironment.Production),
        };

        try
        {
            return Ok(await service.SaveAsync(Text(parameters, "BranchCode") ?? string.Empty, model, User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/BranchTransportSettingsTestAverba")]
    public async Task<IActionResult> TestAverbaAsync([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, AverbaUser, AverbaPassword, AverbaCode, AverbaEnvironment.");

        try
        {
            return Ok(await service.TestAverbaAsync(
                Text(parameters, "BranchCode") ?? string.Empty,
                Text(parameters, "AverbaUser"),
                Text(parameters, "AverbaPassword"),
                Text(parameters, "AverbaCode"),
                (NfeEnvironment)(Number(parameters, "AverbaEnvironment") ?? (int)NfeEnvironment.Production),
                ct));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static int? Number(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is not null ? Convert.ToInt32(value) : null;
}
