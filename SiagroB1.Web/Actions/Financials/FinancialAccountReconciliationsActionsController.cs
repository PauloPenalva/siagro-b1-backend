using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Marcar/desmarcar, fechar, reabrir, excluir e consultar as linhas da reconciliação de conta.</summary>
public class FinancialAccountReconciliationsActionsController(
    FinancialAccountReconciliationsSetLinesService setLines,
    FinancialAccountReconciliationsCloseService close,
    FinancialAccountReconciliationsReopenService reopen,
    FinancialAccountReconciliationsDeleteService delete,
    FinancialAccountReconciliationsGetLinesService getLines) : ODataController
{
    [HttpPost("odata/FinancialAccountReconciliationsSetLines")]
    public Task<IActionResult> SetLinesAsync(ODataActionParameters parameters) => RunAsync(parameters, key =>
    {
        var mark = Refs(parameters, "MarkSettlements", FinancialAccountMovementSource.Settlement)
            .Concat(Refs(parameters, "MarkEntries", FinancialAccountMovementSource.Manual)).ToList();
        var unmark = Refs(parameters, "UnmarkSettlements", FinancialAccountMovementSource.Settlement)
            .Concat(Refs(parameters, "UnmarkEntries", FinancialAccountMovementSource.Manual)).ToList();
        return setLines.ExecuteAsync(key, mark, unmark);
    });

    [HttpPost("odata/FinancialAccountReconciliationsClose")]
    public Task<IActionResult> CloseAsync(ODataActionParameters parameters) =>
        RunAsync(parameters, key => close.ExecuteAsync(key, UserName));

    [HttpPost("odata/FinancialAccountReconciliationsReopen")]
    public Task<IActionResult> ReopenAsync(ODataActionParameters parameters) =>
        RunAsync(parameters, key => reopen.ExecuteAsync(key, UserName));

    [HttpPost("odata/FinancialAccountReconciliationsDelete")]
    public Task<IActionResult> DeleteAsync(ODataActionParameters parameters) =>
        RunAsync(parameters, key => delete.ExecuteAsync(key));

    [HttpGet("odata/FinancialAccountReconciliationsGetLines(Key={key})")]
    public async Task<IActionResult> GetLinesAsync([FromRoute] Guid key)
    {
        try { return Ok(await getLines.ExecuteAsync(key)); }
        catch (Exception e) { return Map(e); }
    }

    private string UserName => User.Identity?.Name ?? "Unknown";

    private async Task<IActionResult> RunAsync(ODataActionParameters? parameters, Func<Guid, Task> action)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");
        if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Informe a reconciliação.");

        try
        {
            await action(Guid.Parse(keyObj.ToString()!));
            return Ok();
        }
        catch (Exception e) { return Map(e); }
    }

    private static IEnumerable<MovementRef> Refs(ODataActionParameters p, string name, FinancialAccountMovementSource source) =>
        p.TryGetValue(name, out var v) && v is IEnumerable<Guid> keys
            ? keys.Select(k => new MovementRef(source, k))
            : [];

    private IActionResult Map(Exception e) => e switch
    {
        NotFoundException or KeyNotFoundException => NotFound(e.Message),
        _ => BadRequest(e.Message)
    };
}
