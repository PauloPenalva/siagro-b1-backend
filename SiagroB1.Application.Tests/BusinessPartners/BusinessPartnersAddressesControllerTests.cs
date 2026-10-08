using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Inclusão avulsa de endereço (<c>POST BusinessPartners('X')/Addresses</c>). A rota é por
/// atributo e não tem entity set: <c>Created</c> montava o Location a partir dele e estourava 500
/// DEPOIS de gravar. O controller tem de responder com o corpo, sem Location.
/// </summary>
public class BusinessPartnersAddressesControllerTests
{
    private sealed class RecordingAddressService : IBusinessPartnerAddressService
    {
        public string? CreatedFor { get; private set; }
        public (string CardCode, string AddressName, string AdresType)? ReadKey { get; private set; }

        public IQueryable<AddressModel> QueryAll(string cardCode) => Enumerable.Empty<AddressModel>().AsQueryable();
        public Task<AddressModel?> GetByIdAsync(string cardCode, string addressName, string adresType)
        {
            ReadKey = (cardCode, addressName, adresType);
            return Task.FromResult<AddressModel?>(new AddressModel
            {
                CardCode = cardCode, AddressName = addressName, AdresType = adresType,
            });
        }
        public Task<AddressModel> Create(string cardCode, AddressModel addressModel)
        {
            CreatedFor = cardCode;
            return Task.FromResult(addressModel);
        }
        public Task<AddressModel> Update(string cardCode, string addressName, string adresType, AddressModel addressModel) =>
            Task.FromResult(addressModel);
        public Task<bool> Delete(string cardCode, string addressName, string adresType) => Task.FromResult(true);
    }

    [Fact]
    public async Task Post_returns_the_address_without_a_location_header()
    {
        var service = new RecordingAddressService();
        var controller = new BusinessPartnersAddressesController(service);
        var model = new AddressModel { AddressName = "FATURAMENTO", AdresType = "B", State = "PR" };

        var result = await controller.PostAsync("'C90001'", model);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(model, ok.Value);
        Assert.Equal("C90001", service.CreatedFor);
    }

    /// <summary>
    /// A tela não manda o <c>CardCode</c> no corpo (ele vem da URL). Sem preenchê-lo, a resposta
    /// levava a chave nula e o serializador do OData estourava DEPOIS de gravar: 500 dentro do
    /// <c>$batch</c> do /edit, com a linha gravada e a tela achando que falhou.
    /// </summary>
    [Fact]
    public async Task Post_fills_the_card_code_of_the_key_in_the_response()
    {
        var controller = new BusinessPartnersAddressesController(new RecordingAddressService());
        var model = new AddressModel { AddressName = "ENTREGA", AdresType = "S", Country = "BR" };

        var result = await controller.PostAsync("'C90001'", model);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("C90001", Assert.IsType<AddressModel>(ok.Value).CardCode);
    }

    /// <summary>
    /// Dentro do <c>$batch</c> o segmento de chave chega SEM decodificar (<c>'END%20TESTE'</c>), e a
    /// busca não achava o endereço de nome com espaço: o UI5 lê assim as colunas que não vieram no
    /// primeiro request, e o /edit abria com Número/Complemento/Município/CEP vazios e um 500.
    /// Apóstrofo vem dobrado no literal do OData (<c>'D''AGUA'</c>).
    /// </summary>
    [Theory]
    [InlineData("'END%20TESTE'", "END TESTE")]
    [InlineData("'END TESTE'", "END TESTE")]
    [InlineData("'D''AGUA'", "D'AGUA")]
    [InlineData("'D%27%27AGUA'", "D'AGUA")]
    [InlineData("'OLHO D'''", "OLHO D'")]
    [InlineData("'FATURAMENTO'", "FATURAMENTO")]
    public async Task Get_decodes_the_key_segments_of_the_route(string rawName, string expected)
    {
        var service = new RecordingAddressService();
        var controller = new BusinessPartnersAddressesController(service);

        await controller.GetAsync("'C90001'", rawName, "'S'", "'C90001'");

        Assert.Equal(("C90001", expected, "S"), service.ReadKey);
    }
}
