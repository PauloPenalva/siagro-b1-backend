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

        public IQueryable<AddressModel> QueryAll(string cardCode) => Enumerable.Empty<AddressModel>().AsQueryable();
        public Task<AddressModel?> GetByIdAsync(string cardCode, string addressName, string adresType) =>
            Task.FromResult<AddressModel?>(null);
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
}
