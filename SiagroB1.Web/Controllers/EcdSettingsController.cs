using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Ecd;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Parâmetros da ECD (SPED Contábil) por filial. Só STANDALONE.</summary>
public class EcdSettingsController(IEcdSettingsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpGet("odata/EcdSettingsGet(branchCode={branchCode})")]
    public async Task<IActionResult> GetAsync(string branchCode)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Filial sem gravação: devolve os padrões para a tela preencher.
        return Ok(await service.GetAsync(branchCode?.Trim() ?? string.Empty)
                  ?? new EcdSettings { BranchCode = branchCode?.Trim() ?? string.Empty });
    }

    [HttpPost("odata/EcdSettingsSave")]
    public async Task<IActionResult> SaveAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Parâmetros nulos = corpo que o OData não conseguiu ler; gravar assim zeraria os parâmetros.
        if (parameters is null) return BadRequest("Informe os parâmetros da ECD.");

        var settings = new EcdSettings
        {
            BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
            BookNumber = int.TryParse(Text(parameters, "BookNumber"), out var book) ? book : 1,
            HasNire = bool.TryParse(Text(parameters, "HasNire"), out var hasNire) && hasNire,
            Nire = Text(parameters, "Nire"),
            ConstitutiveActsDate = CalendarDate(parameters.GetValueOrDefault("ConstitutiveActsDate")),
            LargeCompany = bool.TryParse(Text(parameters, "LargeCompany"), out var large) && large,
            ReferentialPlanCode = Text(parameters, "ReferentialPlanCode") ?? "1",
            SignerName = Text(parameters, "SignerName") ?? string.Empty,
            SignerCpf = Text(parameters, "SignerCpf") ?? string.Empty,
            SignerQualificationCode = Text(parameters, "SignerQualificationCode") ?? "203",
            SignerEmail = Text(parameters, "SignerEmail") ?? string.Empty,
            SignerPhone = Text(parameters, "SignerPhone"),
        };

        try { return Ok(await service.SaveAsync(settings, User.Identity?.Name ?? "Unknown")); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    /// <summary>Dia do calendário como o usuário escolheu (sem converter fuso).</summary>
    private static DateTime? CalendarDate(object? value) => value switch
    {
        DateTimeOffset dto => dto.Date,
        DateTime dt => dt.Date,
        Microsoft.OData.Edm.Date d => new DateTime(d.Year, d.Month, d.Day),
        string s when DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) => parsed.Date,
        _ => null,
    };

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
