using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesInvoices;

/// <summary>
/// Resolve o CFOP de uma linha para a TELA mostrar assim que a natureza é escolhida.
///
/// É só antecipação: quem grava e congela o CFOP continua sendo o
/// <see cref="SalesInvoicesCreateService"/>, com a mesma regra e o mesmo serviço. Sem isto o
/// campo fica vazio durante a digitação, porque o valor só existia depois de salvar.
/// </summary>
public class SalesInvoicesResolveCfopController(
    SalesInvoicesCfopResolveService service
    )
    : ODataController
{
    [HttpGet("odata/SalesInvoicesResolveCfop(UsageCode={usageCode},BranchCode={branchCode},CardCode={cardCode})")]
    public async Task<IActionResult> GetAsync(
        [FromRoute] int usageCode,
        [FromRoute] string branchCode,
        [FromRoute] string cardCode)
    {
        try
        {
            // Rotas por atributo entregam o segmento COM as aspas simples do OData.
            var cfop = await service.ResolveAsync(
                usageCode, branchCode?.Trim('\''), cardCode?.Trim('\''));

            return Ok(cfop);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }

            return BadRequest(e.Message);
        }
    }
}
