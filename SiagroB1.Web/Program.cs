using System.Data;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Batch;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Users;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Interceptors;
using SiagroB1.Infra.WhatsApp;
using SiagroB1.Web.Security;
using SiagroB1.Security.Authentication;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Services;
using SiagroB1.Web.Extensions;
using SiagroB1.Web.ODataConfig;
using SiagroB1.Web.Sockets.TruckScale;

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


var modelBuilder = new ODataConventionModelBuilder
{
    Namespace = "SIAGROB1"
};

builder.Services.AddDbContext<CommonDbContext>(options => 
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroCommon"),
        b =>
        {
            b.MigrationsAssembly("SiagroB1.Migrations");
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("SiagroDB"),
            b =>
            {
                b.MigrationsAssembly("SiagroB1.Migrations");
                b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        options.AddInterceptors(new FinishedContractMutationGuardInterceptor());
        options.EnableSensitiveDataLogging();
    }
);

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("SiagroDB");
    return new SqlConnection(connectionString);
});

builder.Services.AddScoped<IUnitOfWork,  UnitOfWork>();
builder.Services.AddScoped<UserService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = "BasicAuthentication";
        options.DefaultScheme = "BasicAuthentication";
    })
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
        "BasicAuthentication", options => { });

var erp = builder.Configuration["Erp"] ?? "STANDALONE";

switch (erp.ToUpper().Trim())
{ 
    case "SAPB1":
        builder.Services.AddDbContext<SapErpDbContext>(options => 
            options.UseSqlServer(
                builder.Configuration.GetConnectionString("SapDB"),
                sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        );
        
        builder.Services.AddDbContext<SapCommonDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("SapCommon")));
        
        builder.Services.AddSapServices();

        // Espelhamento do cadastro de usuários do SAP: só existe neste modo, porque depende do
        // SapErpDbContext registrado logo acima.
        builder.Services.AddScoped<SapUserSyncService>();
        builder.Services.AddScoped<ISapUserSyncJob, SapUserSyncJob>();
        break;
    
    case "STANDALONE":
        
        builder.Services.AddStandAloneServices();
        break;
    
    default:
        throw new DefaultException("ERP não suportado. Verifique a configuração no appsettings.json");
}

builder.Services.AddApplicationServices();

builder.Services.AddHangfire(config =>
    config.UseSqlServerStorage(builder.Configuration.GetConnectionString("SiagroDB")));

builder.Services.AddHangfireServer(options => options.Queues = ["default"]);

// Fila dedicada com UM worker: envio de WhatsApp em rajada é o que faz um provedor
// não-oficial banir o número da empresa. Serializar os envios é a contenção principal.
builder.Services.AddHangfireServer(options =>
{
    options.Queues = [ContractNotificationDispatchJob.QueueName];
    options.WorkerCount = 1;
});

// Primeiro HttpClient do solution. Instância e token do PlugZapi vão no PATH da URL, montados
// a cada requisição pelo sender — por isso só o BaseAddress fica aqui.
builder.Services.AddHttpClient<IWhatsAppSender, PlugZapiWhatsAppSender>(client =>
{
    var baseUrl = builder.Configuration["Notifications:WhatsApp:BaseUrl"]
                  ?? "https://api.plugzapi.com.br";

    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

modelBuilder.ConfigureODataEntities();

builder.Services.AddControllers().AddOData(options =>
{
    options.EnableQueryFeatures(null);
    options.AddRouteComponents("odata", modelBuilder.GetEdmModel(), new DefaultODataBatchHandler());
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseRequestLocalization(localizationOptions);

app.UseODataBatching();
app.UseRouting();

app.Use(async (context, next) =>
{
    var contentType = context.Request.ContentType;
    if (contentType != null && contentType.Contains("IEEE754Compatible"))
    {
        context.Request.ContentType = "application/json";
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}

app.UseWebSockets();
app.MapTruckScaleWebSocket();

app.UseCookieAuth();
app.UseAuthentication();
app.UseAuthorization();

// DEPOIS de UseAuthorization, de propósito: o dashboard estava registrado antes de
// UseAuthentication, então qualquer filtro de autorização leria um User vazio e não protegeria
// nada. Os argumentos dos jobs de notificação são só o Guid da outbox — telefone e texto da
// mensagem nunca aparecem aqui. Mantenha essa invariante.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()],
});

app.UseRouting()
    .UseEndpoints(endpoints => endpoints
        .MapControllers()
        .WithOpenApi()
    );

RecurringJob.AddOrUpdate<IStorageAddressesDailyCalculationJob>(
    "storage-daily-calculation-job",
    job => job.ExecuteAsync(null, CancellationToken.None),
    "0 1 * * *");

// O varredor é o mecanismo de ENTREGA das notificações, não uma rede de segurança: enfileirar
// direto no serviço de mutação faria o worker poder ler a outbox antes do COMMIT da transação
// de negócio, não achar a linha e desistir em silêncio.
RecurringJob.AddOrUpdate<IContractNotificationSweepJob>(
    "contract-notification-sweep",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Minutely());

// Em modo SAPB1 o cadastro de usuários é mantido no SAP. A varredura é o único caminho que
// enxerga quem sumiu do OUSR - o provisionamento no login só vê quem está entrando.
if (string.Equals(erp.Trim(), "SAPB1", StringComparison.OrdinalIgnoreCase))
{
    RecurringJob.AddOrUpdate<ISapUserSyncJob>(
        "sap-user-sync",
        job => job.ExecuteAsync(CancellationToken.None),
        "*/15 * * * *");
}
else
{
    // Trocar o modo de integração não pode deixar um job órfão tentando ler um banco do SAP
    // que não está mais configurado.
    RecurringJob.RemoveIfExists("sap-user-sync");
}

await app.RunAsync();
