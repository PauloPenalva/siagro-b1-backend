using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Batch;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// A lista de Empresas dava 404 para o administrador: o <c>Get</c> por código tinha o parâmetro de rota
/// chamado <c>code</c> e a convenção do OData o pendurava TAMBÉM em <c>odata/Companies</c>. A coleção
/// executava o Get de um registro com código nulo → <c>NotFound()</c>. Sem login aparecia 403 (a checagem
/// de admin vinha antes), o que escondia o defeito.
/// </summary>
public class ODataCollectionRoutesTests
{
    private static IReadOnlyList<RouteEndpoint> BuildEndpoints()
    {
        var modelBuilder = new ODataConventionModelBuilder();
        modelBuilder.ConfigureODataEntities();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(CompaniesController).Assembly)
            .AddOData(options =>
            {
                options.EnableQueryFeatures(null);
                options.AddRouteComponents("odata", modelBuilder.GetEdmModel(), new DefaultODataBatchHandler());
            });

        var app = builder.Build();
        app.MapControllers();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static bool IsGet(RouteEndpoint e) =>
        e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false;

    [Fact]
    public void Companies_collection_route_runs_the_list_action()
    {
        var actions = BuildEndpoints()
            .Where(e => e.RoutePattern.RawText == "odata/Companies" && IsGet(e))
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!)
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Empty(a.Parameters));
    }

    /// <summary>
    /// Editar (PATCH/PUT) e excluir (DELETE) chegam por <c>odata/Companies('0001')</c>: a convenção do OData
    /// só liga a chave ao parâmetro chamado <c>key</c>. Com <c>code</c>, essas rotas não existiam.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void Companies_key_route_runs_the_action_with_the_key(string method)
    {
        var actions = BuildEndpoints()
            .Where(e => e.RoutePattern.RawText == "odata/Companies({key})" &&
                        (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false))
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!)
            .ToList();

        var action = Assert.Single(actions);
        Assert.Contains(action.Parameters, p => p.Name == "key");
    }
}
