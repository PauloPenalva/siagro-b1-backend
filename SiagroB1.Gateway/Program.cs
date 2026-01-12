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

if (!builder.Environment.IsDevelopment())
{
    builder.Host.UseWindowsService();
    builder.Logging.AddEventLog();
}

builder.Services.AddDbContext<CommonDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroCommon"),
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

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
        var path = ctx.Context.Request.Path.Value;
    
        if (path.EndsWith("index.html"))
        {
            ctx.Context.Response.Headers["Cache-Control"] =
                "no-cache, no-store, must-revalidate";
        }
        else
        {
            ctx.Context.Response.Headers["Cache-Control"] =
                "public,max-age=31536000,immutable";
        }
    }
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/index.html"))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }

    await next();
});


app.UseRouting();
app.UseCookieAuth();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapReverseProxy();



await app.RunAsync();
