using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Ecf;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Parâmetros da ECF (lucro real) por filial. Só STANDALONE.</summary>
public class EcfSettingsController(IEcfSettingsService service, IConfiguration configuration) : ODataController
{
    private static readonly string[] RequiredKeys =
        ["BranchCode", "AssessmentForm", "MainCnae", "LegalEntityQualification", "NatureCode", "PreviousEcfHash"];

    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpGet("odata/EcfSettingsGet(branchCode={branchCode})")]
    public async Task<IActionResult> GetAsync(string branchCode)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Filial sem gravação: devolve os padrões para a tela preencher.
        return Ok(await service.GetAsync(branchCode?.Trim() ?? string.Empty)
                  ?? new EcfSettings { BranchCode = branchCode?.Trim() ?? string.Empty });
    }

    [HttpPost("odata/EcfSettingsSave")]
    public async Task<IActionResult> SaveAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null) return BadRequest("Informe os parâmetros da ECF.");

        // O upsert grava tudo: payload parcial zeraria campos em silêncio. Anuláveis precisam estar presentes.
        foreach (var key in RequiredKeys)
            if (!parameters.ContainsKey(key)) return BadRequest($"Parâmetro {key} ausente.");

        var form = ParseForm(Text(parameters, "AssessmentForm"));
        if (form is null) return BadRequest("Forma de apuração inválida.");

        var settings = new EcfSettings
        {
            BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
            AssessmentForm = form.Value,
            MainCnae = Text(parameters, "MainCnae") ?? string.Empty,
            LegalEntityQualification = Text(parameters, "LegalEntityQualification") ?? string.Empty,
            NatureCode = Text(parameters, "NatureCode") ?? string.Empty,
            PreviousEcfHash = Text(parameters, "PreviousEcfHash"),
        };

        try { return Ok(await service.SaveAsync(settings, User.Identity?.Name ?? "Unknown")); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    /// <summary>Aceita o nome do enum ("Annual") ou o número.</summary>
    private static EcfAssessmentForm? ParseForm(string? text) =>
        Enum.TryParse<EcfAssessmentForm>(text, true, out var f) && Enum.IsDefined(f) ? f : null;

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
