using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShippingTransactions;

public class ShippingTransactionsChangeReleaseController(
    ShippingTransactionsChangeReleaseService changeReleaseService
    ) : ODataController
{
    [HttpPost("odata/ShippingTransactionsChangeRelease")]
    public async Task<ActionResult> ChangeRelease(ODataActionParameters parameters)
    {
        try
        {
            // Parâmetro ausente do corpo deixa `parameters` NULO; string presente pode vir null.
            if (parameters == null)
                return BadRequest("Parâmetros obrigatórios não informados.");

            if (!parameters.TryGetValue("SalesStorageTransactionKeys", out var salesObj) ||
                salesObj is not IEnumerable<Guid> salesKeys ||
                !parameters.TryGetValue("TargetShipmentReleaseKeys", out var targetsObj) ||
                targetsObj is not IEnumerable<Guid> targetKeys)
            {
                return BadRequest("Selecione ao menos um romaneio para trocar a liberação.");
            }

            var sales = salesKeys.ToList();
            var targets = targetKeys.ToList();

            if (sales.Count != targets.Count)
                return BadRequest("Cada romaneio precisa de exatamente uma liberação de destino.");

            parameters.TryGetValue("Reason", out var reasonObj);
            var reason = reasonObj as string;

            var items = sales.Zip(targets, (s, t) => new ShippingReleaseChangeItem(s, t)).ToList();
            var userName = User.Identity?.Name ?? "Unknown";

            await changeReleaseService.ExecuteAsync(items, reason, userName);

            return Ok(new { Count = items.Count });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            // Vários romaneios em lote: outro usuário pode ter alterado carga/romaneio entre a
            // validação e a escrita. A exceção pode chegar direto ou embrulhada (ex.: rollback).
            if (e is DbUpdateConcurrencyException || e.InnerException is DbUpdateConcurrencyException)
            {
                return BadRequest(
                    "Os romaneios foram alterados por outro usuário. Recarregue a carga e tente novamente.");
            }

            return BadRequest(e.Message);
        }
    }
}
