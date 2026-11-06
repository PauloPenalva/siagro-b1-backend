using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Web.Filters;

namespace SiagroB1.Web.Base;

/// <summary>
/// Base dos cadastros OData do Mercado: leitura livre; POST/PUT/PATCH/DELETE exigem MARKET_MANAGE (checado em
/// <see cref="Guard"/>, único ponto de cada gravação). BusinessException → 400 e ForbiddenException → 403, ambos
/// com a mensagem em texto puro; a base genérica devolveria 500 para BusinessException.
/// </summary>
[MarketModuleEnabled]
public abstract class MarketODataController<T, ID>(IBaseService<T, ID> service, MarketPermissionGuard guard)
    : ODataBaseController<T, ID>(service) where T : class
{
    protected string UserName => User.Identity?.Name ?? string.Empty;

    /// <summary>Executa uma gravação: exige MARKET_MANAGE e traduz as exceções de negócio.</summary>
    protected async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try
        {
            await guard.EnsureAsync(UserName, PermissionCodes.MarketManage);
            return await action();
        }
        catch (ForbiddenException e)
        {
            return StatusCode(StatusCodes.Status403Forbidden, e.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception e) when (e is BusinessException or DefaultException)
        {
            return BadRequest(e.Message);
        }
        catch (Exception e)
        {
            return StatusCode(500, e.Message);
        }
    }

    protected virtual Task SaveAsync(ID key, T entity) => GetService().UpdateAsync(key, entity);

    protected virtual Task<T> AddAsync(T entity) => GetService().CreateAsync(entity);

    protected virtual Task<bool> RemoveAsync(ID key) => GetService().DeleteAsync(key);

    public override Task<IActionResult> Post([FromBody] T entity)
    {
        if (!ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(ModelState));

        return Guard(async () =>
        {
            await AddAsync(entity);
            return Created(entity);
        });
    }

    public override Task<IActionResult> Put([FromRoute] ID key, [FromBody] T entity)
    {
        if (!ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(ModelState));

        return Guard(async () =>
        {
            await SaveAsync(key, entity);
            return NoContent();
        });
    }

    public override Task<IActionResult> Delete([FromRoute] ID key) =>
        Guard(async () => await RemoveAsync(key) ? NoContent() : NotFound());

    [AcceptVerbs("PATCH", "MERGE")]
    public override Task<IActionResult> Patch([FromODataUri] ID key, [FromBody] Delta<T> patch)
    {
        if (!ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(ModelState));

        return Guard(async () =>
        {
            var entity = await GetService().GetByIdAsync(key);
            if (entity is null)
                return NotFound();

            patch.Patch(entity);
            await SaveAsync(key, entity);
            return NoContent();
        });
    }
}
