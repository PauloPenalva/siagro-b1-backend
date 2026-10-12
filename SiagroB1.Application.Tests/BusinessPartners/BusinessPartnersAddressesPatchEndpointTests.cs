using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Edição de endereço já gravado no /edit do parceiro. <c>AddressModel</c> não tem entity set, então o
/// UI5 manda o PATCH pela URL de navegação <c>BusinessPartners('X')/Addresses(...)</c>. O método Patch
/// do controller não tinha rota declarada (os irmãos POST/PUT/DELETE/GET tinham): o PATCH dava 404 e a
/// tela mostrava "Communication error: 404" no caminho da propriedade alterada (<c>.../Street</c>).
/// Host mínimo com o EDM real e o controller real.
/// </summary>
public class BusinessPartnersAddressesPatchEndpointTests
{
    private sealed class RecordingAddressService : IBusinessPartnerAddressService
    {
        public (string CardCode, string AddressName, string AdresType, AddressModel Model)? Updated { get; private set; }

        public IQueryable<AddressModel> QueryAll(string cardCode) => Enumerable.Empty<AddressModel>().AsQueryable();
        public Task<AddressModel?> GetByIdAsync(string cardCode, string addressName, string adresType) =>
            Task.FromResult<AddressModel?>(new AddressModel
            {
                CardCode = cardCode, AddressName = addressName, AdresType = adresType,
                Street = "RUA ANTIGA", City = "ITAPETININGA", State = "SP",
            });
        public Task<AddressModel> Create(string cardCode, AddressModel addressModel) => Task.FromResult(addressModel);
        public Task<AddressModel> Update(string cardCode, string addressName, string adresType, AddressModel addressModel)
        {
            Updated = (cardCode, addressName, adresType, addressModel);
            return Task.FromResult(addressModel);
        }
        public Task<bool> Delete(string cardCode, string addressName, string adresType) => Task.FromResult(true);
    }

    private readonly RecordingAddressService _service = new();

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Development",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddScoped<IBusinessPartnerAddressService>(_ => _service);

        var modelBuilder = new ODataConventionModelBuilder();
        modelBuilder.ConfigureODataEntities();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(BusinessPartnersAddressesController).Assembly)
            .AddOData(o =>
            {
                o.EnableQueryFeatures(null);
                o.AddRouteComponents("odata", modelBuilder.GetEdmModel());
            });

        var app = builder.Build();
        app.UseRouting();
        app.MapControllers();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Patch_through_the_navigation_url_updates_the_address()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var request = new HttpRequestMessage(HttpMethod.Patch,
            "/odata/BusinessPartners('FTESTEPJ')/Addresses(AddressName='FATURAMENTO',AdresType='B',CardCode='FTESTEPJ')")
        {
            Content = new StringContent("""{"Street":"AV SERAFINO FERREIRA"}""", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{response.StatusCode}: {body}");
        Assert.NotNull(_service.Updated);
        Assert.Equal(("FTESTEPJ", "FATURAMENTO", "B"),
            (_service.Updated!.Value.CardCode, _service.Updated.Value.AddressName, _service.Updated.Value.AdresType));
        Assert.Equal("AV SERAFINO FERREIRA", _service.Updated.Value.Model.Street);
        Assert.Equal("ITAPETININGA", _service.Updated.Value.Model.City);
    }
}
