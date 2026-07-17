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

public class ShipmentReleasesCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    // Não deve ser chamado — o guard de contrato encerrado lança antes.
    private sealed class ThrowingWarehouseService : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => throw new NotImplementedException();
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => throw new NotImplementedException();
    }

    private ShipmentReleasesCreateService Service() => new(
        _db, new ThrowingWarehouseService(), NullLogger<ShipmentReleasesCreateService>.Instance);

    [Fact]
    public async Task ExecuteAsync_ContractFinished_Throws()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 1000m,
            Status = ContractStatus.Finished,
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var release = new ShipmentRelease
        {
            PurchaseContractKey = pc.Key,
            DeliveryLocationCode = "01",
        };

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(release, "tester"));
    }
}
