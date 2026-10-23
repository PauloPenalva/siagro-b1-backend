using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// CRUD dos cadastros da ECF. A base devolve 500 para qualquer exceção que não seja DefaultException; aqui a
/// validação de negócio (BusinessException) vira 400 com a mensagem em pt-BR. Só STANDALONE.
/// </summary>
public abstract class EcfCrudController<T>(IBaseService<T, Guid> service, IConfiguration configuration)
    : ODataBaseController<T, Guid>(service) where T : class
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    public override async Task<IActionResult> Post([FromBody] T entity)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await GetService().CreateAsync(entity);
            return Created(entity);
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }

    public override async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] T entity)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await GetService().UpdateAsync(key, entity);
            return NoContent();
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary><c>override</c> obrigatório: sem ele o roteamento continua caindo no Patch da base.</summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public override async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<T> patch)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var current = await GetService().GetByIdAsync(key);
        if (current is null) return NotFound();
        try
        {
            patch.Patch(current);
            await GetService().UpdateAsync(key, current);
            return NoContent();
        }
        catch (BusinessException e) { return BadRequest(e.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
