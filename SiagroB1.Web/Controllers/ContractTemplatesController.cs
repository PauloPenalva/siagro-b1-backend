using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.ContractTemplates;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// A base (`ODataBaseController`) engole toda exceção do serviço num `catch (Exception)` e devolve
/// 500 para o que não é `DefaultException`. Por isso a validação de placeholders roda AQUI, antes
/// de delegar — a `BusinessException` vira 400 com a mensagem pt-BR, como o editor espera.
/// </summary>
public class ContractTemplatesController(ContractTemplateService service)
    : ODataBaseController<ContractTemplate, Guid>(service)
{
    public override Task<IActionResult> Post([FromBody] ContractTemplate entity) =>
        Validated(entity, () => base.Post(entity));

    public override Task<IActionResult> Put([FromRoute] Guid key, [FromBody] ContractTemplate entity) =>
        Validated(entity, () => base.Put(key, entity));

    [AcceptVerbs("PATCH", "MERGE")]
    public override async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<ContractTemplate> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var t = await service.GetByIdAsync(key);
        if (t == null)
            return NotFound();

        try
        {
            patch.Patch(t);

            ContractTemplateService.Validate(t);

            await service.UpdateAsync(key, t);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
                return BadRequest(ex.Message);

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    private static async Task<IActionResult> Validated(ContractTemplate entity, Func<Task<IActionResult>> next)
    {
        try { ContractTemplateService.Validate(entity); }
        catch (BusinessException e) { return new BadRequestObjectResult(e.Message); }

        return await next();
    }
}
