using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.Actions.Nfe;

public class BranchNfeSettingsSaveController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsSave")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Environment, DefaultSeries, Series, Descriptions, NextNumbers.");

        try
        {
            var numbers = Integers(parameters, "Series");
            var descriptions = Strings(parameters, "Descriptions");
            var nextNumbers = Integers(parameters, "NextNumbers");

            if (numbers.Count != descriptions.Count || numbers.Count != nextNumbers.Count)
                return BadRequest("As listas de séries chegaram incompletas.");

            var series = numbers
                .Select((number, i) => new BranchNfeSeriesInput(number, descriptions[i], nextNumbers[i]))
                .ToList();

            return Ok(await service.SaveAsync(
                parameters["BranchCode"]?.ToString() ?? string.Empty,
                (NfeEnvironment)Convert.ToInt32(parameters["Environment"]),
                Convert.ToInt32(parameters["DefaultSeries"]),
                series,
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static List<int> Integers(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not IEnumerable sequence)
            return [];

        var result = new List<int>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                _ => int.TryParse(item?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new DefaultException("As listas de séries chegaram incompletas."),
            });
        }

        return result;
    }

    private static List<string> Strings(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not IEnumerable sequence)
            return [];

        return sequence.Cast<object?>().Select(x => x?.ToString() ?? string.Empty).ToList();
    }
}
