using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Cobre a coluna "Nome Fantasia" da primeira tela da Expedição de Grãos: o AliasName é
/// opcional no SAP e alguns depositantes não têm, então a célula não pode ficar vazia.
/// </summary>
public class ShipmentReleasesBalanceServiceFNameTests
{
    private const string ItemCode = "MILHO";
    private const string WarehouseCode = "F024756";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed class FakeWarehouseService(Dictionary<string, WarehouseInfo> warehouses) : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => throw new NotImplementedException();
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => Task.FromResult(warehouses);
    }

    private async Task SeedAsync()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = ItemCode,
            ItemName = "Milho", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            DeliveryLocationCode = WarehouseCode, TotalVolume = 1000m,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key,
            DeliveryLocationCode = WarehouseCode, DeliveryLocationName = "MF COMERCIO DE CEREAIS LTDA",
            ReleasedQuantity = 100m, Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();
    }

    private ShipmentReleasesBalanceService Service(WarehouseInfo warehouse) =>
        new(_db,
            new FakeWarehouseService(new Dictionary<string, WarehouseInfo> { [WarehouseCode] = warehouse }),
            NullLogger<ShipmentReleasesBalanceService>.Instance);

    [Fact]
    public async Task FName_FallsBackToCardName_WhenAliasNameIsEmpty()
    {
        await SeedAsync();

        var warehouse = new WarehouseInfo
        {
            CardCode = WarehouseCode, CardName = "MF COMERCIO DE CEREAIS LTDA",
            CardFName = null, TaxId = "12345678000199",
        };

        var row = Assert.Single(await Service(warehouse).ExecuteAsync(ItemCode));

        Assert.Equal("MF COMERCIO DE CEREAIS LTDA", row.FName);
        Assert.Equal("12345678000199", row.TaxId);
    }

    [Fact]
    public async Task FName_KeepsAliasName_WhenItIsFilled()
    {
        await SeedAsync();

        var warehouse = new WarehouseInfo
        {
            CardCode = WarehouseCode, CardName = "CAPAL COOPERATIVA AGROINDUSTRIAL",
            CardFName = "CAPAL TAQUARITUBA",
        };

        var row = Assert.Single(await Service(warehouse).ExecuteAsync(ItemCode));

        Assert.Equal("CAPAL TAQUARITUBA", row.FName);
    }
}
