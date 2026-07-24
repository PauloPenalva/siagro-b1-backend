using System.Security.Claims;

namespace SiagroB1.Web.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Lê a claim <c>IsAdmin</c>, gravada como o ToString() de um bool ("True"/"False") tanto pelo
    /// BasicAuthenticationHandler quanto pelo CookieAuthMiddleware — os dois caminhos por onde a
    /// identidade chega ao Web.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal? user) =>
        bool.TryParse(user?.FindFirst("IsAdmin")?.Value, out var isAdmin) && isAdmin;
}
