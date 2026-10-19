using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Actions.NfeEmail;

/// <summary>
/// Reenvio manual do e-mail da NF-e pela nota (só STANDALONE): uma ação por documento e a função que
/// traz os e-mails do parceiro para pré-preencher o diálogo.
/// </summary>
public class NfeEmailController(NfeEmailSendService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "O envio de NF-e por e-mail só existe no modo STANDALONE.";

    [HttpPost("odata/SalesInvoicesSendNfeEmail")]
    public Task<IActionResult> SendSalesAsync([FromBody] ODataActionParameters parameters) =>
        SendAsync(NfeEmailDocumentType.Sales, parameters);

    [HttpPost("odata/PurchaseInvoicesSendNfeEmail")]
    public Task<IActionResult> SendPurchaseAsync([FromBody] ODataActionParameters parameters) =>
        SendAsync(NfeEmailDocumentType.Purchase, parameters);

    /// <summary>DocumentType: 1 = Sales, 2 = Purchase.</summary>
    [HttpGet("odata/NfeEmailDefaultRecipients(DocumentType={documentType},Key={key})")]
    public async Task<IActionResult> DefaultRecipientsAsync([FromRoute] int documentType, [FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);

        if (!Enum.IsDefined((NfeEmailDocumentType)documentType))
            return BadRequest("Tipo de documento inválido.");

        try
        {
            return Ok(await service.DefaultRecipientsAsync((NfeEmailDocumentType)documentType, key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }

    private async Task<IActionResult> SendAsync(NfeEmailDocumentType type, ODataActionParameters parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);

        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        var recipients = parameters.TryGetValue("Recipients", out var list) && list is IEnumerable<object?> items
            ? items.Select(x => x?.ToString() ?? string.Empty).ToList()
            : [];

        try
        {
            var row = await service.SendAsync(type, key, recipients, User.Identity?.Name ?? "Unknown");
            return Ok(new { row.Key, Status = (int)row.Status });
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
