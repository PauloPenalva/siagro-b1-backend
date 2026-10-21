using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.Accounting;

/// <summary>
/// Salvar/excluir versão de regra de distribuição. As linhas chegam em listas paralelas
/// (LineCostCenterCodes/LineAccountingItemCodes/LinePercentages; vazio = sem código), como nas demais actions de topo do projeto.
/// </summary>
public class DistributionRuleVersionsActionsController(IDistributionRuleService service) : ODataController
{
    [HttpPost("odata/DistributionRulesSaveVersion")]
    public async Task<IActionResult> SaveVersionAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("Code") || !parameters.ContainsKey("ValidFrom"))
            return BadRequest("Parâmetros obrigatórios: Code, ValidFrom, LineCostCenterCodes, LineAccountingItemCodes, LinePercentages.");

        try
        {
            var centers = Strings(parameters, "LineCostCenterCodes");
            var items = Strings(parameters, "LineAccountingItemCodes");
            var percentages = Decimals(parameters, "LinePercentages");

            // Listas ausentes valem como "nenhum código" em todas as linhas.
            if (centers.Count == 0) centers = percentages.Select(_ => string.Empty).ToList();
            if (items.Count == 0) items = percentages.Select(_ => string.Empty).ToList();

            if (centers.Count != percentages.Count || items.Count != percentages.Count)
                return BadRequest("As linhas da regra chegaram incompletas.");

            var version = new DistributionRuleVersion
            {
                ValidFrom = ToDate(parameters["ValidFrom"]),
                Lines = centers
                    .Select((center, i) => new DistributionRuleLine
                    {
                        CostCenterCode = center,
                        AccountingItemCode = items[i],
                        Percentage = percentages[i],
                    })
                    .ToList(),
            };

            var saved = await service.SaveVersionAsync(parameters["Code"]?.ToString() ?? string.Empty, version, Today());

            return Ok(new { saved.Id, saved.RuleCode, saved.ValidFrom });
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/DistributionRulesDeleteVersion")]
    public async Task<IActionResult> DeleteVersionAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("VersionId")?.ToString(), out var versionId))
            return BadRequest("Informe a versão.");

        try
        {
            await service.DeleteVersionAsync(versionId, Today());

            return NoContent();
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    // Data de Brasília, como no NF-e.
    private static DateTime Today() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone).Date;

    private static DateTime ToDate(object? value) => value switch
    {
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTime dt => dt.Date,
        DateTimeOffset dto => dto.Date,
        _ when DateTime.TryParse(value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed.Date,
        _ => throw new DefaultException("Data de início inválida."),
    };

    private static List<string> Strings(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not IEnumerable sequence)
            return [];

        return sequence.Cast<object?>().Select(x => x?.ToString() ?? string.Empty).ToList();
    }

    private static List<decimal> Decimals(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not IEnumerable sequence)
            return [];

        return sequence.Cast<object?>().Select(item => item switch
        {
            decimal m => m,
            double d => (decimal)d,
            int i => i,
            long l => l,
            _ => decimal.TryParse(item?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new DefaultException("As linhas da regra chegaram incompletas."),
        }).ToList();
    }
}
