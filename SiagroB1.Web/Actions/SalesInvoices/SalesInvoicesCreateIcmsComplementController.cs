using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Actions.Nfe;

namespace SiagroB1.Web.Actions.SalesInvoices;

/// <summary>
/// "Complementar ICMS" (spec 2026-10-18 S2): cria o complemento de ICMS Pendente e devolve a chave dele.
/// <c>IcmsBases</c> e <c>IcmsValues</c> são arrays PARALELOS a <c>OriginItemKeys</c> (contagens diferentes = erro de
/// montagem, recusado aqui), como no <c>SalesInvoicesCreateNfeReturn</c>.
/// </summary>
public class SalesInvoicesCreateIcmsComplementController(SalesInvoicesCreateIcmsComplementService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesCreateIcmsComplement")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        var itemKeys = parameters.TryGetValue("OriginItemKeys", out var keysObj) && keysObj is IEnumerable<Guid> keys
            ? keys.ToList()
            : [];

        try
        {
            var bases = NfeReturnActionParameters.Decimals(parameters, "IcmsBases", "Base do ICMS inválida");
            var values = NfeReturnActionParameters.Decimals(parameters, "IcmsValues", "Valor do ICMS inválido");

            if (itemKeys.Count == 0)
                return BadRequest(SalesInvoicesCreateIcmsComplementService.NoItemInformed);

            if (bases.Count != itemKeys.Count || values.Count != itemKeys.Count)
                return BadRequest("A lista de itens e as de base e ICMS têm tamanhos diferentes.");

            var created = await service.ExecuteAsync(key, itemKeys, bases, values, User?.Identity?.Name ?? "Unknown");

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
