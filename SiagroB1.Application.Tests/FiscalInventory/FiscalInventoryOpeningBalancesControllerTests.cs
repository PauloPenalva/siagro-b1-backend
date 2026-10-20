using Microsoft.AspNetCore.OData.Results;
using SiagroB1.Application.Services.FiscalInventory;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.FiscalInventory;

public class FiscalInventoryOpeningBalancesControllerTests
{
    private static FiscalInventoryOpeningBalancesController Controller(string erp) =>
        new(new FiscalInventoryOpeningBalancesService(TestDb.CreateUnitOfWork()), TestConfiguration.Erp(erp));

    [Fact]
    public async Task Every_verb_is_refused_in_sapb1_mode()
    {
        var controller = Controller("SAPB1");

        Assert.IsType<BadRequestODataResult>(controller.Get());
        Assert.IsType<BadRequestODataResult>(await controller.Get(Guid.NewGuid()));
        Assert.IsType<BadRequestODataResult>(await controller.Post(new FiscalInventoryOpeningBalance()));
        Assert.IsType<BadRequestODataResult>(await controller.Delete(Guid.NewGuid()));
    }

    [Fact]
    public async Task Post_maps_validation_error_to_bad_request_in_standalone()
    {
        var result = await Controller("STANDALONE").Post(new FiscalInventoryOpeningBalance());

        var bad = Assert.IsType<BadRequestODataResult>(result);
        Assert.Equal("Informe a filial.", bad.Error.Message);
    }
}
