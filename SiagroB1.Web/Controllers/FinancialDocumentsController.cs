using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class FinancialDocumentsController(
    FinancialDocumentsGetService getService,
    FinancialDocumentsManualCreateService createService,
    FinancialDocumentsManualUpdateService updateService,
    FinancialDocumentsManualDeleteService deleteService) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 5)]
    public ActionResult<IEnumerable<FinancialDocument>> Get() => Ok(getService.QueryAll());

    // Rota literal nas DUAS formas: a não declarada toma 404 no UI5.
    [HttpGet("odata/FinancialDocuments({key:guid})")]
    [HttpGet("odata/FinancialDocuments/{key:guid}")]
    [EnableQuery(MaxExpansionDepth = 5)]
    public async Task<ActionResult<FinancialDocument>> Get([FromRoute] Guid key)
    {
        var entity = await getService.GetByIdAsync(key);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpGet("odata/FinancialDocuments({key:guid})/Settlements")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/Settlements")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialSettlement>> GetSettlements([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.Settlements));

    [HttpGet("odata/FinancialDocuments({key:guid})/ChangeLogs")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialDocumentChangeLog>> GetChangeLogs([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.ChangeLogs));

    /// <summary>Inclusão manual de título (Contas a Pagar/Receber). Os títulos do sistema nascem pelos seus serviços.</summary>
    public async Task<IActionResult> Post([FromBody] FinancialDocument entity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(created);
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    /// <summary>Edição do título manual: só os campos do formulário entram; os demais do delta são ignorados.</summary>
    [HttpPatch("odata/FinancialDocuments({key:guid})")]
    [HttpPatch("odata/FinancialDocuments/{key:guid}")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<FinancialDocument> patch)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (patch is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        var changed = patch.GetChangedPropertyNames().ToHashSet();
        T? Value<T>(string name) => changed.Contains(name) && patch.TryGetPropertyValue(name, out var v) ? (T?)v : default;
        string? Text(string name) => Value<string>(name);

        try
        {
            var changes = new ManualFinancialDocumentChanges
            {
                CardCode = Text(nameof(FinancialDocument.CardCode)),
                BranchCode = Text(nameof(FinancialDocument.BranchCode)),
                DocumentDate = changed.Contains(nameof(FinancialDocument.DocumentDate))
                    ? Value<DateTime>(nameof(FinancialDocument.DocumentDate)) : null,
                DueDate = changed.Contains(nameof(FinancialDocument.DueDate))
                    ? Value<DateTime>(nameof(FinancialDocument.DueDate)) : null,
                Currency = changed.Contains(nameof(FinancialDocument.Currency))
                    ? Value<CurrencyType>(nameof(FinancialDocument.Currency)) : null,
                NetAmount = changed.Contains(nameof(FinancialDocument.NetAmount))
                    ? Value<decimal>(nameof(FinancialDocument.NetAmount)) : null,
                HasTaxDocumentNumber = changed.Contains(nameof(FinancialDocument.TaxDocumentNumber)),
                TaxDocumentNumber = Text(nameof(FinancialDocument.TaxDocumentNumber)),
                HasPaymentTermsText = changed.Contains(nameof(FinancialDocument.PaymentTermsText)),
                PaymentTermsText = Text(nameof(FinancialDocument.PaymentTermsText)),
                HasComments = changed.Contains(nameof(FinancialDocument.Comments)),
                Comments = Text(nameof(FinancialDocument.Comments)),
            };

            var updated = await updateService.ExecuteAsync(key, changes, User.Identity?.Name ?? "Unknown");
            return Updated(updated);
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    [HttpDelete("odata/FinancialDocuments({key:guid})")]
    [HttpDelete("odata/FinancialDocuments/{key:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private IActionResult Fail(Exception ex) => ex switch
    {
        NotFoundException or KeyNotFoundException => NotFound(ex.Message),
        DefaultException or ApplicationException => BadRequest(ex.Message),
        _ => StatusCode(500, ex.Message)
    };
}
