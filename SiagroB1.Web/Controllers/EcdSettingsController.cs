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
    private static readonly string[] RequiredKeys =
    [
        "BranchCode", "BookNumber", "HasNire", "Nire", "ConstitutiveActsDate", "LargeCompany",
        "ReferentialPlanCode", "SignerName", "SignerCpf", "SignerQualificationCode", "SignerEmail", "SignerPhone",
    ];

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

        // Payload parcial zeraria campos em silêncio (o upsert grava tudo): todas as chaves da action são
        // obrigatórias; as anuláveis precisam estar presentes, mas podem vir nulas.
        foreach (var key in RequiredKeys)
            if (!parameters.ContainsKey(key)) return BadRequest($"Parâmetro {key} ausente.");

        if (!int.TryParse(Text(parameters, "BookNumber"), out var book)) return BadRequest("Número do livro inválido.");
        if (!bool.TryParse(Text(parameters, "HasNire"), out var hasNire)) return BadRequest("Parâmetro HasNire inválido.");
        if (!bool.TryParse(Text(parameters, "LargeCompany"), out var large)) return BadRequest("Parâmetro LargeCompany inválido.");

        var settings = new EcdSettings
        {
            BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
            BookNumber = book,
            HasNire = hasNire,
            Nire = Text(parameters, "Nire"),
            ConstitutiveActsDate = CalendarDate(parameters["ConstitutiveActsDate"]),
            LargeCompany = large,
            ReferentialPlanCode = Text(parameters, "ReferentialPlanCode") ?? string.Empty,
            SignerName = Text(parameters, "SignerName") ?? string.Empty,
            SignerCpf = Text(parameters, "SignerCpf") ?? string.Empty,
            SignerQualificationCode = Text(parameters, "SignerQualificationCode") ?? string.Empty,
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
