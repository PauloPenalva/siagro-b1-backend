using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Configurações contábeis (data de corte, conta de apuração, periodicidade). Só STANDALONE.</summary>
public class AccountingSettingsController(IAccountingSettingsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpGet("odata/AccountingSettingsGet")]
    public async Task<IActionResult> GetAsync()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(await service.GetAsync());
    }

    [HttpPost("odata/AccountingSettingsSave")]
    public async Task<IActionResult> SaveAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        // Parâmetros nulos = corpo que o OData não conseguiu ler; gravar assim zeraria as configurações.
        if (parameters is null) return BadRequest("Informe as configurações contábeis.");

        var settings = new AccountingSettings
        {
            CutoverDate = CalendarDate(parameters.GetValueOrDefault("CutoverDate")),
            DefaultResultClosingAccountCode = Text(parameters, "DefaultResultClosingAccountCode"),
            ClosingFrequency = int.TryParse(Text(parameters, "ClosingFrequency"), out var f)
                ? (AccountingClosingFrequency)f
                : AccountingClosingFrequency.Monthly,
        };

        try { return Ok(await service.SaveAsync(settings, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    /// <summary>
    /// Dia do calendário como o usuário escolheu: de um DateTimeOffset pega a data no próprio offset (convertê-lo
    /// para o fuso do servidor levaria 01/01 00:00Z para 31/12).
    /// </summary>
    private static DateTime? CalendarDate(object? value) => value switch
    {
        DateTimeOffset dto => dto.Date,
        DateTime dt => dt.Date,
        Microsoft.OData.Edm.Date d => new DateTime(d.Year, d.Month, d.Day),
        string s when DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) => parsed.Date,
        _ => null,
    };

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
