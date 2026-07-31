using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Email;
using SiagroB1.Security.Authentication;
using SiagroB1.Security.Interfaces;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Services;
using SiagroB1.Security.Services.SapUsers;
using SiagroB1.Security.Shared;

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
builder.Services.AddSingleton<PasswordPolicy>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Modo de integração: no SAPB1 o cadastro de usuários é mantido no SAP (tabela OUSR) e o
// SiagroB1 apenas o espelha. O provisionamento pontual roda no login e no pedido de recuperação
// de senha, então o Gateway também precisa enxergar o banco do SAP.
var erp = builder.Configuration["Erp"] ?? "STANDALONE";

if (string.Equals(erp.Trim(), "SAPB1", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<SapErpDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("SapDB"),
            // Timeout curto: esta consulta está no caminho do login, e o padrão de 30s deixaria a
            // tela de entrada pendurada sempre que o banco do SAP estivesse indisponível.
            sqlOptions => sqlOptions.CommandTimeout(5)));

    builder.Services.AddScoped<ISapUserProvisioner, SapUserProvisioner>();
}
else
{
    builder.Services.AddScoped<ISapUserProvisioner, NullSapUserProvisioner>();
}

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

WarnIfPasswordRecoveryCannotSendEmail(app);

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
/// Avisa, no boot, que a recuperação de senha não vai entregar nada.
///
/// Sem isto o problema é invisível: o usuário pede o link, a tela responde "enviaremos um link"
/// (mensagem genérica de propósito, para o endpoint público não virar verificador de contas), e
/// nada acontece. O único rastro fica numa linha de log perdida no meio do tráfego.
///
/// Em modo SAPB1 isso é grave: recuperar a senha é o ÚNICO caminho de primeiro acesso para quem
/// veio do OUSR.
/// </summary>
static void WarnIfPasswordRecoveryCannotSendEmail(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Email");
    var configuration = app.Configuration;

    if (!configuration.GetValue("Email:Enabled", false))
    {
        logger.LogWarning(
            "ENVIO DE E-MAIL DESABILITADO (Email:Enabled = false). A recuperação de senha vai " +
            "gerar o link e gravá-lo apenas no log, sem enviá-lo a ninguém.");
        return;
    }

    if (string.IsNullOrWhiteSpace(configuration["Email:Host"]) ||
        string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]))
    {
        logger.LogWarning(
            "E-MAIL HABILITADO MAS INCOMPLETO: Email:Host e/ou Email:FromAddress não " +
            "configurados. Nenhum e-mail de recuperação de senha será entregue.");
    }
}

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
