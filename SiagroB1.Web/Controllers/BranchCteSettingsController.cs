using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.Controllers;

/// <summary>Configuração do CT-e da filial (só STANDALONE): função de leitura + ação de gravar.</summary>
public class BranchCteSettingsController(BranchCteSettingsService service) : ODataController
{
    [HttpGet("odata/BranchCteSettingsGet(BranchCode={branchCode})")]
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

    [HttpPost("odata/BranchCteSettingsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Enabled, Environment, DefaultSeries, DefaultQuantityUnit, " +
                              "Series, Descriptions, NextNumbers.");

        var series = Numbers(parameters, "Series");
        var descriptions = Texts(parameters, "Descriptions");
        var nextNumbers = Numbers(parameters, "NextNumbers");

        if (series.Count != descriptions.Count || series.Count != nextNumbers.Count)
            return BadRequest("Series, Descriptions e NextNumbers precisam ter o mesmo tamanho.");

        var model = new BranchCteSettingsModel
        {
            Enabled = parameters.TryGetValue("Enabled", out var enabled) && enabled is not null && Convert.ToBoolean(enabled),
            Environment = (NfeEnvironment)(Number(parameters, "Environment") ?? (int)NfeEnvironment.Homologation),
            DefaultSeries = Number(parameters, "DefaultSeries") ?? 1,
            DefaultQuantityUnit = parameters.TryGetValue("DefaultQuantityUnit", out var unit) ? unit?.ToString() ?? "00" : "00",
            SeriesList = series
                .Select((number, i) => new BranchCteSeriesModel { Series = number, Description = descriptions[i], NextNumber = nextNumbers[i] })
                .ToList(),
        };

        try
        {
            var branchCode = parameters["BranchCode"]?.ToString() ?? string.Empty;
            return Ok(await service.SaveAsync(branchCode, model, User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static int? Number(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is not null ? Convert.ToInt32(value) : null;

    private static List<int> Numbers(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is IEnumerable sequence
            ? sequence.Cast<object?>().Select(x => Convert.ToInt32(x)).ToList()
            : [];

    private static List<string> Texts(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is IEnumerable sequence
            ? sequence.Cast<object?>().Select(x => x?.ToString() ?? string.Empty).ToList()
            : [];
}
