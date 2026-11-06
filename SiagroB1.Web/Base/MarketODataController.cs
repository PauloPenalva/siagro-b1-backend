using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Web.Filters;

namespace SiagroB1.Web.Base;

/// <summary>
/// Base dos cadastros OData do Mercado: leitura livre; POST/PUT/PATCH/DELETE exigem MARKET_MANAGE (checado em
/// <see cref="Guard"/>, único ponto de cada gravação). BusinessException → 400 e ForbiddenException → 403, ambos
/// com a mensagem em texto puro; a base genérica devolveria 500 para BusinessException. Erros do SQL Server
/// (duplicidade, valor fora do limite) também viram 400 pt-BR. O GET por chave devolve <see cref="SingleResult"/>
/// sobre consulta, para o <c>$expand</c>/<c>$select</c> rodarem no banco (entidade materializada voltaria a
/// navegação vazia).
/// </summary>
[MarketModuleEnabled]
public abstract class MarketODataController<T, ID>(IBaseService<T, ID> service, MarketPermissionGuard guard)
    : ODataBaseController<T, ID>(service) where T : class
{
    public const string DuplicateMessage = "Já existe um registro com estes dados (código ou combinação única repetida).";

    public const string OutOfRangeMessage = "Um dos valores numéricos está fora do limite permitido para o campo.";

    public const string SaveFailedMessage = "Não foi possível gravar o registro. Confira os dados e tente de novo.";

    protected string UserName => User.Identity?.Name ?? string.Empty;

    /// <summary>Permissão exigida nas gravações (MARKET_MANAGE; os limites de exposição exigem a da diretoria).</summary>
    protected virtual string WritePermission => PermissionCodes.MarketManage;

    /// <summary>Propriedade da chave da entidade (o GET por chave filtra por ela).</summary>
    protected virtual string KeyPropertyName => "Id";

    [EnableQuery]
    public override async Task<ActionResult<T>> Get([FromRoute] ID key)
    {
        var query = GetService().QueryAll().Where(KeyEquals(key));
        if (!await query.AnyAsync())
            return NotFound();
        return Ok(SingleResult.Create(query));
    }

    private Expression<Func<T, bool>> KeyEquals(ID key)
    {
        var entity = Expression.Parameter(typeof(T), "e");
        var body = Expression.Equal(Expression.Property(entity, KeyPropertyName), Expression.Constant(key, typeof(ID)));
        return Expression.Lambda<Func<T, bool>>(body, entity);
    }

    /// <summary>Tradução de erro do SQL Server para mensagem pt-BR (null = não é erro de gravação reconhecido).</summary>
    public static string? DescribeSaveError(Exception e)
    {
        for (Exception? current = e; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2627 or 2601 })
                return DuplicateMessage;
            // 8115: estouro aritmético; o SqlClient também recusa antes de enviar ("out of range").
            if (current is SqlException { Number: 8115 }
                || (current is ArgumentException && current.Message.Contains("out of range", StringComparison.OrdinalIgnoreCase)))
                return OutOfRangeMessage;
        }

        return e is DbUpdateException ? SaveFailedMessage : null;
    }

    /// <summary>Executa uma gravação: exige MARKET_MANAGE e traduz as exceções de negócio.</summary>
    protected async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try
        {
            await guard.EnsureAsync(UserName, WritePermission);
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
        catch (Exception e) when (DescribeSaveError(e) is { } message)
        {
            HttpContext?.RequestServices?.GetService<ILogger<MarketODataController<T, ID>>>()
                ?.LogWarning(e, "Falha ao gravar {Entity} do Mercado.", typeof(T).Name);
            return BadRequest(message);
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
