using Microsoft.AspNetCore.Http;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Sessão vencida ou encerrada deixava o SIAGROB1.User (8 h, legível pelo navegador) para trás: o /status
/// o tomava por sessão, as chamadas seguintes davam 401 e a tela de login recarregava em loop.
/// </summary>
public class SessionCookiesTests
{
    private static DefaultHttpContext RequestWith(string cookieHeader)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = cookieHeader;
        return http;
    }

    private static string SetCookies(HttpContext http) => string.Join("\n", http.Response.Headers.SetCookie.ToArray());

    [Fact]
    public void Leftover_user_cookie_is_expired_on_the_root_path()
    {
        var http = RequestWith("SIAGROB1.User=%7B%22Username%22%3A%22admin%22%7D");

        var cleared = SessionCookies.ClearLeftovers(http.Request, http.Response);

        Assert.True(cleared);
        var header = SetCookies(http);
        Assert.Contains("SIAGROB1.User=;", header);
        Assert.Contains("path=/", header);
        Assert.Contains("expires=Thu, 01 Jan 1970", header);
    }

    [Fact]
    public void Leftover_session_cookie_is_expired_too()
    {
        var http = RequestWith("SIAGROB1.Session=abc; SIAGROB1.User=x");

        SessionCookies.ClearLeftovers(http.Request, http.Response);

        var header = SetCookies(http);
        Assert.Contains("SIAGROB1.Session=;", header);
        Assert.Contains("SIAGROB1.User=;", header);
    }

    [Fact]
    public void Request_without_session_cookies_touches_nothing()
    {
        var http = RequestWith("other=1");

        var cleared = SessionCookies.ClearLeftovers(http.Request, http.Response);

        Assert.False(cleared);
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }
}
