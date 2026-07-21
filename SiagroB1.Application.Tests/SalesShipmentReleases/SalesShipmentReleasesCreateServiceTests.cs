using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    // Resolve o nome do armazém; devolve null (DeliveryLocationName fica null, tudo bem).
    private sealed class NullWarehouseService : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => Task.FromResult<WarehouseModel?>(null);
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => throw new NotImplementedException();
    }

    private SalesShipmentReleasesCreateService Service() => new(
        _db, new NullWarehouseService(), NullLogger<SalesShipmentReleasesCreateService>.Instance);

    private async Task<SalesContract> SeedContractAsync(decimal totalVolume, decimal invoicedQuantity)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = totalVolume, Status = ContractStatus.Approved,
        };
        _db.Context.SalesContracts.Add(sc);

        if (invoicedQuantity > 0)
        {
            var inv = new SalesInvoice
            {
                Key = Guid.NewGuid(), CardCode = "C0001",
                InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
            };
            _db.Context.SalesInvoices.Add(inv);
            _db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), SalesInvoiceKey = inv.Key, SalesContractKey = sc.Key,
                ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = invoicedQuantity, DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
            });
        }

        await _db.Context.SaveChangesAsync();
        return sc;
    }

    private SalesShipmentRelease NewRelease(Guid contractKey, decimal released) => new()
    {
        SalesContractKey = contractKey, DeliveryLocationCode = "01", ReleasedQuantity = released,
    };

    [Fact]
    public async Task Create_FullyInvoicedContract_Throws()
    {
        // 600k de contrato, 600k já faturado → saldo físico 0.
        var sc = await SeedContractAsync(totalVolume: 600_000m, invoicedQuantity: 600_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester"));

        Assert.Contains("físico", ex.Message);
        Assert.Empty(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_WithinPhysicalBalance_Succeeds()
    {
        // 1000k de contrato, 600k faturado → saldo físico 400k.
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);

        var created = await Service().ExecuteAsync(NewRelease(sc.Key, 300_000m), "tester");

        Assert.Equal(ReleaseStatus.Pending, created.Status);
        Assert.Single(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_ExceedingPhysicalBalance_Throws()
    {
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 500_000m), "tester"));
    }

    [Fact]
    public async Task Create_SecondReleaseConsumesRemainingPhysical_Throws()
    {
        // físico 400k; 1ª liberação de 300k reserva; a 2ª de 200k não cabe (resta 100k).
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);
        await Service().ExecuteAsync(NewRelease(sc.Key, 300_000m), "tester");

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 200_000m), "tester"));

        // a de 100k ainda cabe
        var ok = await Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester");
        Assert.Equal(ReleaseStatus.Pending, ok.Status);
    }

    [Fact]
    public async Task Create_FinishedContract_Throws()
    {
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 0m);
        sc.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester"));
    }
}
