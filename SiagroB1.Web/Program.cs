using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Batch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Authentication;
using SiagroB1.Web.DI;
using SiagroB1.Web.ODataConfig;

var builder = WebApplication.CreateBuilder(args);

var modelBuilder = new ODataConventionModelBuilder
{
    Namespace = "SIAGROB1"
};

builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
        "BasicAuthentication", null);

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
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("SiagroDB"),
        b =>
        {
            b.MigrationsAssembly("SiagroB1.Migrations");
            b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        })
);

builder.Services.AddScoped<IUnitOfWork,  UnitOfWork>();

builder.Services.AddHttpContextAccessor();

var erp = builder.Configuration["Erp"] ?? "SAPB1";

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
        
        modelBuilder.EntitySet<BusinessPartner>("BusinessPartners");
        modelBuilder.EntitySet<Item>("Items");
        
        builder.Services.AddScoped<IBusinessPartnerService, BusinessPartnerService>();
        builder.Services.AddScoped<IItemService, ItemService>();
        break;
    default:
        throw new DefaultException("ERP não suportado. Verifique a configuração no appsettings.json");
}

builder.Services.AddDomainServices();

modelBuilder.ConfigureODataEntities();

builder.Services.AddControllers().AddOData(options =>
{
    options.EnableQueryFeatures(null);
    options.AddRouteComponents("odata", modelBuilder.GetEdmModel(), new DefaultODataBatchHandler());
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseODataBatching(); // Habilita o suporte a batch, deve ser chamado antes do UseRouting
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

// Define qual pasta do wwwroot será servida
if (!app.Environment.IsDevelopment())
{
    // Serve a versão otimizada de produção
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        DefaultFileNames = ["index.html"],
        FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "app"))
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "app")),
        RequestPath = ""
    });
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication(); // IMPORTANTE: antes de UseAuthorization
app.UseAuthorization();

#pragma warning disable ASP0014
app.UseEndpoints(endpoints =>
    endpoints
    .MapControllers()
    //.RequireAuthorization()
    .WithOpenApi()
);
#pragma warning restore ASP0014

await app.RunAsync();
