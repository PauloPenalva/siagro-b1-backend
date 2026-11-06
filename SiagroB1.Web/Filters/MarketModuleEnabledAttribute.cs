using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SiagroB1.Infra;

namespace SiagroB1.Web.Filters;

/// <summary>
/// Módulo Mercado desligado nesta implantação: 404 com o cabeçalho <c>X-Market-Module: disabled</c> e
/// corpo <c>{"code":"MarketModuleDisabled"}</c>, para a tela distinguir de um 404 do Gateway (rota
/// <c>/market</c> ausente). Filtro de recurso: roda antes do model binding, então um corpo inválido também
/// recebe o 404 e não um 400.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MarketModuleEnabledAttribute : Attribute, IAsyncResourceFilter
{
    public const string HeaderName = "X-Market-Module";
    public const string DisabledBody = "{\"code\":\"MarketModuleDisabled\"}";

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

        if (!MarketModule.IsEnabled(configuration))
        {
            context.HttpContext.Response.Headers[HeaderName] = "disabled";
            // ContentResult, não ObjectResult: nas rotas OData o formatter não serializa tipo anônimo.
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = "application/json",
                Content = DisabledBody,
            };
            return Task.CompletedTask;
        }

        return next();
    }
}
