using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Actions.Nfe;

namespace SiagroB1.Web.Actions.PurchaseFiscalReconciliations;

/// <summary>
/// Ações da Conciliação Fiscal de Compras. <c>InvoiceItemKeys</c>, <c>TransactionKeys</c> e <c>Quantities</c> do Save
/// são arrays PARALELOS (contagens diferentes = erro de montagem). ⚠️ ODataActionParameters chega NULO quando falta
/// parâmetro declarado no EDM.
/// </summary>
public class PurchaseFiscalReconciliationActionsController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpPost("odata/PurchaseFiscalReconciliationSuggest")]
    public Task<IActionResult> Suggest([FromBody] ODataActionParameters parameters) => Run(async () =>
        Ok(await service.SuggestAsync(Guids(parameters, "InvoiceItemKeys"), Guids(parameters, "TransactionKeys"))));

    [HttpPost("odata/PurchaseFiscalReconciliationSave")]
    public Task<IActionResult> Save([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        var items = Guids(parameters, "InvoiceItemKeys");
        var transactions = Guids(parameters, "TransactionKeys");
        var quantities = parameters is null ? [] : NfeReturnActionParameters.Quantities(parameters);
        if (items.Count != transactions.Count || items.Count != quantities.Count)
            return BadRequest("As listas de linhas, romaneios e quantidades têm tamanhos diferentes.");

        var pairs = items.Select((item, i) => new FiscalReconciliationPairDto
            { PurchaseInvoiceItemKey = item, StorageTransactionKey = transactions[i], Quantity = quantities[i] }).ToList();
        await service.SaveAsync(pairs, User.Identity?.Name ?? "Unknown");
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationRemove")]
    public Task<IActionResult> Remove([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "Key", out var key)) return BadRequest("Parâmetro obrigatório: Key.");
        await service.RemoveAsync(key, User.Identity?.Name ?? "Unknown");
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationConfirm")]
    public Task<IActionResult> Confirm([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "TransactionKey", out var key)) return BadRequest("Parâmetro obrigatório: TransactionKey.");
        await service.ConfirmAsync(key, User.Identity?.Name ?? "Unknown");
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationUnconfirm")]
    public Task<IActionResult> Unconfirm([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "TransactionKey", out var key)) return BadRequest("Parâmetro obrigatório: TransactionKey.");
        await service.UnconfirmAsync(key);
        return NoContent();
    });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
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

    private static List<Guid> Guids(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) && value is IEnumerable<Guid> keys ? keys.ToList() : [];

    private static bool TryGuid(ODataActionParameters? parameters, string name, out Guid key)
    {
        key = Guid.Empty;
        return parameters is not null && parameters.TryGetValue(name, out var value) && Guid.TryParse(value?.ToString(), out key);
    }
}
