using Hangfire.Dashboard;

namespace SiagroB1.Web.Security;

/// <summary>
/// Exige usuário autenticado para abrir o painel do Hangfire.
///
/// O painel estava aberto: o <c>SiagroB1.Web</c> escuta em localhost:50000 e, embora o Gateway
/// não faça proxy de <c>/hangfire</c>, qualquer coisa na mesma máquina ou rede o alcançava —
/// incluindo a possibilidade de disparar e apagar jobs.
///
/// Depende de estar registrado DEPOIS de <c>UseAuthentication</c>: antes dela o
/// <c>HttpContext.User</c> ainda está vazio e este filtro liberaria tudo.
/// </summary>
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.Identity?.IsAuthenticated == true;
}
