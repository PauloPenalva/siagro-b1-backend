using System.Data;
using System.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.DI;
using SiagroB1.Reports.PartnerSources;
using SiagroB1.Security.Middlewares;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService();
}

builder.Services.AddLocalization();
var supportedCultures = new [] {"en-US", "pt-BR"};
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("pt-BR")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

builder.Services.AddDbContext<CommonDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroCommon"),
        b =>
        {
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("SiagroDB"),
            b =>
            {
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        options.EnableSensitiveDataLogging();
    }
);

// Modo de integração do deployment, igual ao lido pelo SiagroB1.Web. Em SAPB1 os
// dados de parceiro vivem em OCRD/CRD1 e as tabelas BUSINESS_PARTNERS do Siagro
// ficam vazias — por isso a origem do parceiro é escolhida aqui.
var erp = builder.Configuration.GetValue<string>("Erp") ?? "STANDALONE";

if (string.Equals(erp, "SAPB1", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<SapErpDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("SapDB"),
            b =>
            {
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
    );

    builder.Services.AddScoped<IPartnerSource, SapPartnerSource>();
}
else
{
    builder.Services.AddScoped<IPartnerSource, StandalonePartnerSource>();
}

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("SiagroDB");
    return new SqlConnection(connectionString);
});

FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));

builder.Services.AddScoped<IUnitOfWork,  UnitOfWork>();
builder.Services.AddReportServices();

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseCookieAuth();
//app.UseAuthentication();
//app.UseAuthorization();

app.UseRouting()
    .UseEndpoints(endpoints => endpoints
        .MapControllers()
    );

app.Run();
