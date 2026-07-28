using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Authentication;
using SiagroB1.Security.Interfaces;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Services;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService();
}

builder.Services.AddDbContext<CommonDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroCommon"),
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroDB"),
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BranchService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<MenuService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = "BasicAuthentication";
        options.DefaultScheme = "BasicAuthentication";
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "SIAGROB1";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/security/auth/unauthorized";
        options.AccessDeniedPath = "/security/auth/forbidden";
    })
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
        "BasicAuthentication", options => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AuthenticatedOnly", policy => policy.RequireAuthenticatedUser());

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
var provider = new FileExtensionContentTypeProvider
{
    Mappings =
    {
        [".properties"] = "text/plain"
    }
};

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    OnPrepareResponse = ctx =>
    {
        // Só o que está dentro de uma pasta ~<timestamp>~ pode ser cacheado para sempre:
        // o nome da pasta já identifica a versão do conteúdo e muda a cada build.
        //
        // Todo o resto precisa ser revalidado - em especial o sap-ui-cachebuster-info.json,
        // que é o índice de onde o app lê os timestamps atuais. Cacheá-lo de forma imutável
        // fazia o browser continuar pedindo as URLs ~antigas~ indefinidamente, e só um
        // Ctrl+F5 trazia a versão nova.
        //
        // "no-cache" não impede o armazenamento: o browser guarda e revalida por ETag,
        // recebendo 304 quando nada mudou.
        ctx.Context.Response.Headers.CacheControl = IsVersionedResource(ctx.Context.Request.Path.Value)
            ? "public,max-age=31536000,immutable"
            : "no-cache, must-revalidate";
    }
});

app.UseRouting();
app.UseCookieAuth();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapReverseProxy();

await app.RunAsync();

/// <summary>
/// Indica se o caminho aponta para um recurso versionado pelo cache buster do UI5,
/// isto é, se algum de seus segmentos tem a forma <c>~1765897191667~</c>.
/// </summary>
static bool IsVersionedResource(string? path)
{
    if (string.IsNullOrEmpty(path))
    {
        return false;
    }

    foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
        if (segment.Length > 2 &&
            segment[0] == '~' &&
            segment[^1] == '~' &&
            segment[1..^1].All(char.IsAsciiDigit))
        {
            return true;
        }
    }

    return false;
}
