using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SpedFiscal;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Parâmetros do SPED Fiscal por filial (perfil, contabilista, recolhimento do ICMS). Só STANDALONE.</summary>
public class SpedFiscalSettingsController(ISpedFiscalSettingsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpGet("odata/SpedFiscalSettingsGet(branchCode={branchCode})")]
    public async Task<IActionResult> GetAsync(string branchCode)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Filial sem gravação: devolve os padrões para a tela preencher.
        return Ok(await service.GetAsync(branchCode?.Trim() ?? string.Empty)
                  ?? new SpedFiscalSettings { BranchCode = branchCode?.Trim() ?? string.Empty });
    }

    [HttpPost("odata/SpedFiscalSettingsSave")]
    public async Task<IActionResult> SaveAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Parâmetros nulos = corpo que o OData não conseguiu ler; gravar assim zeraria os parâmetros.
        if (parameters is null) return BadRequest("Informe os parâmetros do SPED Fiscal.");

        var settings = new SpedFiscalSettings
        {
            BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
            Profile = Text(parameters, "Profile") ?? "A",
            ActivityType = int.TryParse(Text(parameters, "ActivityType"), out var activity) ? activity : 1,
            AccountantName = Text(parameters, "AccountantName") ?? string.Empty,
            AccountantCpf = Text(parameters, "AccountantCpf") ?? string.Empty,
            AccountantCrc = Text(parameters, "AccountantCrc") ?? string.Empty,
            AccountantCnpj = Text(parameters, "AccountantCnpj"),
            AccountantZipCode = Text(parameters, "AccountantZipCode"),
            AccountantStreet = Text(parameters, "AccountantStreet"),
            AccountantNumber = Text(parameters, "AccountantNumber"),
            AccountantComplement = Text(parameters, "AccountantComplement"),
            AccountantDistrict = Text(parameters, "AccountantDistrict"),
            AccountantPhone = Text(parameters, "AccountantPhone"),
            AccountantEmail = Text(parameters, "AccountantEmail"),
            AccountantMunicipalityCode = Text(parameters, "AccountantMunicipalityCode"),
            IcmsRevenueCode = Text(parameters, "IcmsRevenueCode") ?? string.Empty,
            IcmsDueDay = int.TryParse(Text(parameters, "IcmsDueDay"), out var day) ? day : 10,
        };

        try { return Ok(await service.SaveAsync(settings, User.Identity?.Name ?? "Unknown")); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
