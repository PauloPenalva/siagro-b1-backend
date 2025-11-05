using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

using SiagroB1.Core.Services;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Core.Interfaces;
using Microsoft.AspNetCore.OData.Batch;
using System.Text.Json.Serialization;
using SiagroB1.Core.Interfaces.SAP;
using SiagroB1.Core.Services.SAP;
using SiagroB1.Domain.Entities.SAP;

var builder = WebApplication.CreateBuilder(args);

var modelBuilder = new ODataConventionModelBuilder
{
    Namespace = "SIAGROB1"
};

builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
);

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
            options.UseNpgsql(builder.Configuration.GetConnectionString("SapCommon")));
        
        modelBuilder.EntitySet<BusinessPartner>("BusinessPartners");
        modelBuilder.EntitySet<Item>("Items");
        
        builder.Services.AddScoped<IBusinessPartnerService, BusinessPartnerService>();
        builder.Services.AddScoped<IItemService, ItemService>();
        break;
    default:
        throw new DefaultException("ERP não suportado. Verifique a configuração no appsettings.json");
}

builder.Services.AddScoped<IStateService, StateService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IUnitOfMeasureService, UnitOfMeasureService>();
builder.Services.AddScoped<IProcessingServiceService, ProcessingServiceService>();
builder.Services.AddScoped<IProcessingCostService, ProcessingCostService>();
builder.Services.AddScoped<IQualityAttribService, QualityAttribService>();
builder.Services.AddScoped<IProcessingCostServiceDetailService, ProcessingCostServiceDetailService>();
builder.Services.AddScoped<IProcessingCostQualityParameterService, ProcessingCostQualityParameterService>();
builder.Services.AddScoped<IProcessingCostDryingParameterService, ProcessingCostDryingParameterService>();
builder.Services.AddScoped<IProcessingCostDryingDetailService, ProcessingCostDryingDetailService>();
builder.Services.AddScoped<IWhareHouseService, WhareHouseService>();
builder.Services.AddScoped<ILoteArmazenagemService, LoteArmazenagemService>();
builder.Services.AddScoped<IHarvestSeasonService, HarvestSeasonService>();
builder.Services.AddScoped<ITruckDriverService, TruckDriverService>();
builder.Services.AddScoped<ITruckService, TruckService>();

modelBuilder.EntitySet<Branch>("Branchs");
modelBuilder.EntitySet<SiagroB1.Domain.Entities.UnitOfMeasure>("UnitsOfMeasure");
modelBuilder.EntitySet<ProcessingService>("ProcessingServices");
modelBuilder.EntitySet<QualityAttrib>("QualityAttribs");
modelBuilder.EntitySet<ProcessingCost>("ProcessingCosts");
modelBuilder.EntitySet<ProcessingCostDryingParameter>("ProcessingCostDryingParameters");
modelBuilder.EntitySet<ProcessingCostDryingDetail>("ProcessingCostDryingDetails");
modelBuilder.EntitySet<ProcessingCostQualityParameter>("ProcessingCostQualityParameters");
modelBuilder.EntitySet<ProcessingCostServiceDetail>("ProcessingCostServiceDetails");
modelBuilder.EntitySet<WhareHouse>("WhareHouses");
modelBuilder.EntitySet<StorageLot>("StorageLots");
modelBuilder.EntitySet<HarvestSeason>("HarvestSeasons");
modelBuilder.EntitySet<TruckDriver>("TruckDrivers");
modelBuilder.EntitySet<State>("States");
modelBuilder.EntitySet<Truck>("Trucks");

var edmModel =  modelBuilder.GetEdmModel();
//EdmModelAutoAnnotations.ApplyAllAnnotations((EdmModel) edmModel, typeof(Participante).Assembly, "SIAGROB1");

builder.Services.AddControllers().AddOData(options =>
{
    options.EnableQueryFeatures(null);
    options.AddRouteComponents("odata", edmModel, new DefaultODataBatchHandler());
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

#pragma warning disable ASP0014
app.UseEndpoints(endpoints =>
    endpoints
    .MapControllers()
    .WithOpenApi()
);
#pragma warning restore ASP0014

await app.RunAsync();
