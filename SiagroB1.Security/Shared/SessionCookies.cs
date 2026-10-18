using Microsoft.AspNetCore.Http;

namespace SiagroB1.Security.Shared;

/// <summary>
/// Cookies que o login grava: a sessão (HttpOnly, validada no banco) e o SIAGROB1.User (legível pelo
/// navegador, só para exibir o nome — não autentica nada).
/// </summary>
public static class SessionCookies
{
    public const string Session = "SIAGROB1.Session";
    public const string User = "SIAGROB1.User";

    /// <summary>
    /// Requisição sem identidade que ainda traz cookie de sessão: a sessão venceu ou foi encerrada no banco.
    /// Expira os dois cookies (no caminho "/", o mesmo do login). Sem isso o SIAGROB1.User sobrevivia à
    /// sessão e a tela de login recarregava em loop. Devolve se havia algo a limpar.
    /// </summary>
    public static bool ClearLeftovers(HttpRequest request, HttpResponse response)
    {
        var cleared = false;
        var options = new CookieOptions { Path = "/" };

        foreach (var name in new[] { Session, User })
        {
            if (!request.Cookies.ContainsKey(name))
                continue;

            response.Cookies.Delete(name, options);
            cleared = true;
        }

        return cleared;
    }
}
