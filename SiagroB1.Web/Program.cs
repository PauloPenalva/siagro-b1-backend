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
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.ESignature.D4Sign;
using SiagroB1.Infra.Interceptors;
using SiagroB1.Infra.Pdf;
using SiagroB1.Infra.WhatsApp;
using SiagroB1.Web.Security;
using SiagroB1.Security.Authentication;
using SiagroB1.Security.Middlewares;
using SiagroB1.Security.Services;
using SiagroB1.Web.Extensions;
using SiagroB1.Web.Hooks;
using SiagroB1.Web.ODataConfig;
using SiagroB1.Web.Sockets.TruckScale;
using SiagroB1.Web.Startup;

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

// Assinatura eletrônica. HttpClient tipado como o do WhatsApp; credenciais são lidas a cada
// chamada pelo provider, por isso só o endereço e o timeout ficam aqui.
builder.Services.AddHttpClient<IESignatureProvider, D4SignProvider>(client =>
{
    var baseUrl = builder.Configuration["Signature:D4Sign:BaseUrl"]
                  ?? "https://sandbox.d4sign.com.br/api/v1";

    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// PDF das minutas por Chromium headless. Singleton: um browser por processo, páginas por render.
builder.Services.AddSingleton<IHtmlToPdfRenderer, ChromiumHtmlToPdfRenderer>();

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

// Antes de qualquer job ou requisição: com migration pendente o Web se recusa a subir. O `dotnet ef`
// encerra o host dentro do Build(), então isto não trava o próprio `database update`.
await PendingMigrationsGuard.EnsureNoPendingMigrationsAsync(app);

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
app.MapD4SignWebhook();

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

// Rede de segurança do webhook do D4Sign — só faz sentido rodar com a assinatura habilitada.
if (app.Configuration.GetValue("Signature:Enabled", false))
{
    RecurringJob.AddOrUpdate<ContractDraftsReconcileJob>(
        ContractDraftsReconcileJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        ContractDraftsReconcileJob.CronExpression);
}
else
{
    // Desligar a assinatura não pode deixar job órfão chamando um provedor não configurado.
    RecurringJob.RemoveIfExists(ContractDraftsReconcileJob.RecurringJobId);
}

WarnIfTruckScaleChannelIsUnauthenticated(app);
WarnIfContractDraftPdfIsUnavailable(app);
WarnIfD4SignWebhookIsUnprotected(app);

await app.RunAsync();

/// <summary>
/// Avisa, no boot, que o canal WebSocket da balança aceita qualquer um.
///
/// Sem isto o problema é invisível: tudo funciona igual. Só que, com o caminho publicado pelo
/// Gateway, quem descobrir o código de uma balança recebe a configuração do indicador e passa a
/// injetar peso no LiveReadingStore - que é de onde `POST /scales/{code}/capture` emite o
/// comprovante. Peso forjado vira romaneio, exatamente o que o comprovante existe para impedir.
///
/// Continua liberado por padrão para não quebrar as instalações que seguem só em rede interna.
/// </summary>
static void WarnIfTruckScaleChannelIsUnauthenticated(WebApplication app)
{
    if (!string.IsNullOrWhiteSpace(app.Configuration[ScaleClientAuth.ConfigurationKey]))
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("TruckScaleWebSocket")
        .LogWarning(
            "CANAL DA BALANÇA SEM AUTENTICAÇÃO ({ConfigurationKey} não configurada). Qualquer um " +
            "que alcance /ws/truck-scale pode ler a configuração do indicador e injetar peso.",
            ScaleClientAuth.ConfigurationKey);
}

/// <summary>
/// Avisa, no boot, que o PDF de minutas vai falhar: sem Chromium configurado nem baixado, o primeiro
/// download de minuta dispara um download de ~150 MB (ou falha sem internet). Não derruba o serviço —
/// o resto do sistema não depende disso.
/// </summary>
static void WarnIfContractDraftPdfIsUnavailable(WebApplication app)
{
    if (ChromiumHtmlToPdfRenderer.IsAvailable(app.Configuration))
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ContractDraftPdf")
        .LogWarning(
            "CHROMIUM NÃO ENCONTRADO para PDF de minutas ({Key} vazio ou inválido). O primeiro PDF " +
            "vai tentar baixar o Chromium para {Path}; sem internet, falha.",
            ChromiumHtmlToPdfRenderer.ChromiumPathKey, Path.Combine(AppContext.BaseDirectory, "chromium"));
}

/// <summary>
/// Avisa que o webhook do D4Sign vai recusar tudo. Sem segredo configurado o endpoint é
/// fail-closed (401), então o estado das minutas só avança pelo job de reconciliação — que
/// roda a cada 30 min. Não derruba o serviço.
/// </summary>
static void WarnIfD4SignWebhookIsUnprotected(WebApplication app)
{
    if (!app.Configuration.GetValue("Signature:Enabled", false))
        return;
    if (!string.IsNullOrWhiteSpace(app.Configuration[D4SignWebhookEndpoint.SecretKey]))
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("D4SignWebhook")
        .LogWarning(
            "WEBHOOK DO D4SIGN SEM SEGREDO ({Key} vazia) com assinatura habilitada. O endpoint " +
            "recusa tudo com 401; as minutas só avançam pela reconciliação a cada 30 minutos.",
            D4SignWebhookEndpoint.SecretKey);
}
