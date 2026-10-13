using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageCustody;

/// <summary>"Gerar nota" no romaneio: cria a nota de custódia Pendente e devolve a chave do documento.</summary>
public class StorageTransactionsCreateCustodyDocumentController(StorageTransactionsCreateCustodyDocumentService service) : ODataController
{
    [HttpPost("odata/StorageTransactionsCreateCustodyDocument")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM. Role vai como o NOME do enum.
        if (parameters is null
            || !parameters.TryGetValue("StorageTransactionKey", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key)
            || !parameters.TryGetValue("Role", out var roleObj) || !Enum.TryParse<StorageCustodyRole>(roleObj?.ToString(), out var role)
            || role == StorageCustodyRole.None
            || !parameters.TryGetValue("UsageCode", out var usageObj) || usageObj is null
            || !parameters.TryGetValue("UnitPrice", out var priceObj) || priceObj is null)
            return BadRequest("Parâmetros obrigatórios: StorageTransactionKey, Role, UsageCode, UnitPrice.");

        try
        {
            var created = await service.ExecuteAsync(
                new StorageCustodyDocumentRequest(key, role, Convert.ToInt32(usageObj, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(priceObj, CultureInfo.InvariantCulture)),
                User.Identity?.Name ?? "Unknown");
            return Ok(created);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
