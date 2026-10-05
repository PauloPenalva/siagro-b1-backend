using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// "Devolver": cria a devolução própria Pendente e devolve a chave dela. <c>Quantities</c> e <c>ItemNumbers</c> são arrays
/// PARALELOS a <c>OriginItemKeys</c> (contagens diferentes = erro de montagem, recusado aqui).
/// </summary>
public class PurchaseInvoicesCreateNfeReturnController(PurchaseInvoicesNfeReturnCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesCreateNfeReturn")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        var itemKeys = parameters.TryGetValue("OriginItemKeys", out var keysObj) && keysObj is IEnumerable<Guid> keys
            ? keys.ToList()
            : [];
        var quantities = NfeReturnActionParameters.Quantities(parameters);
        var itemNumbers = NfeReturnActionParameters.ItemNumbers(parameters);

        if (itemKeys.Count == 0)
            return BadRequest("Informe a quantidade a devolver de ao menos um item.");

        if (quantities.Count != itemKeys.Count)
            return BadRequest("A lista de itens e a de quantidades têm tamanhos diferentes.");

        if (itemNumbers.Count != itemKeys.Count)
            return BadRequest("A lista de itens e a de números de item têm tamanhos diferentes.");

        // ⚠️ TryGetValue devolve true com valor NULO: o ToString direto estoura.
        var reason = parameters.TryGetValue("Reason", out var reasonObj) ? reasonObj?.ToString() ?? string.Empty : string.Empty;

        try
        {
            var created = await service.ExecuteAsync(
                new PurchaseInvoiceNfeReturnRequest(
                    key, itemKeys.Select((itemKey, i) => new PurchaseInvoiceNfeReturnItem(itemKey, quantities[i], itemNumbers[i] == 0 ? null : itemNumbers[i])).ToList(), reason),
                User.Identity?.Name ?? "Unknown");

            return Ok(created.Key);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }
}
