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
//using Microsoft.OData.Edm;
//using SiagroB1.Web.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CommonDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("CommonConnection")));

builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
);

var modelBuilder = new ODataConventionModelBuilder
{
    Namespace = "SIAGROB1"
};

var erp = builder.Configuration["Erp"] ?? "STANDALONE";

switch (erp.ToUpper().Trim())
{
    case "STANDALONE":
        builder.Services.AddScoped<IFilialService, FilialService>();
        builder.Services.AddScoped<IContaContabilService, ContaContabilService>();
        builder.Services.AddScoped<IParticipanteService, ParticipanteService>();
        builder.Services.AddScoped<IUnidadeMedidaService, UnidadeMedidaService>();
        builder.Services.AddScoped<IProdutoService, ProdutoService>();
        builder.Services.AddScoped<IServicoArmazemService, ServicoArmazemService>();
        builder.Services.AddScoped<ITabelaCustoService, TabelaCustoService>();
        builder.Services.AddScoped<ICaracteristicaQualidadeService, CaracteristicaQualidadeService>();
        builder.Services.AddScoped<ITabelaCustoServicoService, TabelaCustoServicoService>();
        builder.Services.AddScoped<ITabelaCustoQualidadeService, TabelaCustoQualidadeService>();
        builder.Services.AddScoped<ITabelaCustoDescontoSecagemService, TabelaCustoDescontoSecagemService>();
        builder.Services.AddScoped<ITabelaCustoValorSecagemService, TabelaCustoValorSecagemService>();
        builder.Services.AddScoped<IArmazemService, ArmazemService>();
        builder.Services.AddScoped<ILoteArmazenagemService, LoteArmazenagemService>();
        builder.Services.AddScoped<ISafraService, SafraService>();
        break;
    case "SAPB1":
        //builder.Services.AddHttpClient<IFilialService, SapFilialService>();
        //builder.Services.AddHttpClient<IContaContabilService, SapContaContabilService>();
        //builder.Services.AddHttpClient<IParticipanteService, SapParticipanteService>();
        break;
    default:
        throw new DefaultException("ERP não suportado. Verifique a configuração no appsettings.json");
}

modelBuilder.EntitySet<Participante>("Participantes");
modelBuilder.EntitySet<Filial>("Filiais");
modelBuilder.EntitySet<ContaContabil>("ContasContabeis");
modelBuilder.EntitySet<UnidadeMedida>("UnidadesMedida");
modelBuilder.EntitySet<Produto>("Produtos");
modelBuilder.EntitySet<ServicoArmazem>("ServicosArmazem");
modelBuilder.EntitySet<CaracteristicaQualidade>("CaracteristicasQualidade");
modelBuilder.EntitySet<TabelaCusto>("TabelasCusto");
modelBuilder.EntitySet<TabelaCustoDescontoSecagem>("DescontosSecagem");
modelBuilder.EntitySet<TabelaCustoValorSecagem>("ValoresSecagem");
modelBuilder.EntitySet<TabelaCustoQualidade>("Qualidades");
modelBuilder.EntitySet<TabelaCustoServico>("Servicos");
modelBuilder.EntitySet<Armazem>("Armazens");
modelBuilder.EntitySet<LoteArmazenagem>("LotesArmazenagem");
modelBuilder.EntitySet<Safra>("Safras");
    
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
