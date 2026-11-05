using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SiagroB1.Infra;

namespace SiagroB1.Web.Filters;

/// <summary>Módulo Mercado desligado nesta implantação: a rota some (404), como se não existisse.</summary>
public sealed class MarketModuleEnabledAttribute : ActionFilterAttribute
{
    public override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

        if (!MarketModule.IsEnabled(configuration))
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        return next();
    }
}
