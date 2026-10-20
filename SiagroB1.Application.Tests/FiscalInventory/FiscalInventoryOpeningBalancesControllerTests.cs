using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
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

        var results = new IActionResult[]
        {
            controller.Get(),
            await controller.Get(Guid.NewGuid()),
            await controller.Post(new FiscalInventoryOpeningBalance()),
            await controller.Patch(Guid.NewGuid(), new Delta<FiscalInventoryOpeningBalance>()),
            await controller.Delete(Guid.NewGuid()),
        };

        foreach (var result in results)
        {
            var bad = Assert.IsType<BadRequestODataResult>(result);
            Assert.Equal("Disponível somente no modo STANDALONE.", bad.Error.Message);
        }
    }

    [Fact]
    public async Task Post_maps_validation_error_to_bad_request_in_standalone()
    {
        var result = await Controller("STANDALONE").Post(new FiscalInventoryOpeningBalance());

        var bad = Assert.IsType<BadRequestODataResult>(result);
        Assert.Equal("Informe a filial.", bad.Error.Message);
    }

    [Fact]
    public async Task Post_returns_created_in_standalone()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        await db.SaveChangesAsync();
        var controller = new FiscalInventoryOpeningBalancesController(
            new FiscalInventoryOpeningBalancesService(db), TestConfiguration.Erp("STANDALONE"));

        var result = await controller.Post(new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", BalanceDate = new DateTime(2026, 1, 1),
            Quantity = 10m, TotalValue = 100m, UnitOfMeasureCode = "KG",
        });

        Assert.IsType<CreatedODataResult<FiscalInventoryOpeningBalance>>(result);
    }

    [Fact]
    public async Task Patch_maps_validation_error_to_bad_request_in_standalone()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        var entity = new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", BalanceDate = new DateTime(2026, 1, 1),
            Quantity = 10m, TotalValue = 100m, UnitOfMeasureCode = "KG",
        };
        db.Context.FiscalInventoryOpeningBalances.Add(entity);
        await db.SaveChangesAsync();
        var controller = new FiscalInventoryOpeningBalancesController(
            new FiscalInventoryOpeningBalancesService(db), TestConfiguration.Erp("STANDALONE"));
        var patch = new Delta<FiscalInventoryOpeningBalance>();
        patch.TrySetPropertyValue(nameof(FiscalInventoryOpeningBalance.Quantity), 0m);

        var result = await controller.Patch(entity.Key, patch);

        var bad = Assert.IsType<BadRequestODataResult>(result);
        Assert.Equal("Informe uma quantidade maior que zero.", bad.Error.Message);
    }
}
