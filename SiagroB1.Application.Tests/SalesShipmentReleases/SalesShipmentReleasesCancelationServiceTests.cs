using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesCancelationServiceTests
{
    private const string Reason = "troca de armazém";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesCancelationService Service() =>
        new(_db.Context,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            NullLogger<SalesShipmentReleasesCancelationService>.Instance);

    private async Task<SalesShipmentRelease> SeedReleaseAsync(
        ReleaseStatus status = ReleaseStatus.Actived,
        decimal released = 100m,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = released, Status = contractStatus,
        };
        var sr = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key,
            DeliveryLocationCode = "01", ReleasedQuantity = released, Status = status,
        };
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    /// <summary>
    /// Consumo da liberação agora vem do ledger de alocações (faturamento grava a linha).
    /// </summary>
    private async Task AddAllocationAsync(Guid releaseKey, decimal volume)
    {
        _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = Guid.NewGuid(),
            SalesInvoiceItemKey = Guid.NewGuid(),
            SalesShipmentReleaseKey = releaseKey,
            Volume = volume,
            Origin = SalesContractAllocationOrigin.Billing,
        });
        await _db.Context.SaveChangesAsync();
    }

    private Task<SalesShipmentRelease> ReloadAsync(Guid key) =>
        _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Cancel_ActivedWithSale_Succeeds_AndConsumesOnlyShipped()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddAllocationAsync(sr.Key, 300m);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Cancelled, saved.Status);
        Assert.Equal(300m, saved.ShippedQuantity);
        Assert.Equal(300m, saved.ConsumedQuantity);
        Assert.Equal(700m, saved.ReturnedToContractQuantity);
        Assert.Equal(0m, saved.AvailableQuantity);
    }

    [Fact]
    public async Task Cancel_StampsAuditFieldsAndReason()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key, "maria", "  troca de armazém  ");

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal("troca de armazém", saved.CancellationReason);
        Assert.Equal("maria", saved.CanceledBy);
        Assert.NotNull(saved.CanceledAt);
    }

    [Fact]
    public async Task Cancel_WithZeroBalance_ThrowsSuggestingFinalize()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddAllocationAsync(sr.Key, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));

        Assert.Contains("Finalizar", ex.Message);
        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Cancel_FinishedContract_Throws()
    {
        var sr = await SeedReleaseAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Cancel_WithoutReason_Throws(string? reason)
    {
        var sr = await SeedReleaseAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", reason!));
    }

    [Theory]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Cancel_FromTerminalStatus_Throws(ReleaseStatus status)
    {
        var sr = await SeedReleaseAsync(status: status);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));
    }

    [Fact]
    public async Task Cancel_NoTransactions_Cancels_AndConsumesNothing()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Cancelled, saved.Status);
        Assert.Equal(0m, saved.ConsumedQuantity);
    }
}
