using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesBackfillDeliveryLocationNameServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesBackfillDeliveryLocationNameService Service(IBusinessPartnerService businessPartners) =>
        new(_db.Context, businessPartners);

    [Fact]
    public async Task Backfill_FillsBlankNamesFromBusinessPartner_LeavesFilledUntouched()
    {
        _db.Context.SalesShipmentReleases.AddRange(
            new SalesShipmentRelease
            {
                Key = Guid.NewGuid(), SalesContractKey = Guid.NewGuid(),
                DeliveryLocationCode = "C002255", DeliveryLocationName = null,
            },
            new SalesShipmentRelease
            {
                Key = Guid.NewGuid(), SalesContractKey = Guid.NewGuid(),
                DeliveryLocationCode = "C009999", DeliveryLocationName = "Já Preenchido",
            });
        await _db.Context.SaveChangesAsync();

        var businessPartners = new FakeBusinessPartnerService(new()
        {
            ["C002255"] = "4R SILVICULTURA LTDA - EPP",
            ["C009999"] = "OUTRO CLIENTE",
        });

        var result = await Service(businessPartners).ExecuteAsync();

        // Só a de nome em branco entra no escopo; a já preenchida não é tocada.
        Assert.Equal(1, result.Scanned);
        Assert.Equal(1, result.Updated);

        var filled = await _db.Context.SalesShipmentReleases.SingleAsync(x => x.DeliveryLocationCode == "C002255");
        Assert.Equal("4R SILVICULTURA LTDA - EPP", filled.DeliveryLocationName);

        var untouched = await _db.Context.SalesShipmentReleases.SingleAsync(x => x.DeliveryLocationCode == "C009999");
        Assert.Equal("Já Preenchido", untouched.DeliveryLocationName);
    }

    [Fact]
    public async Task Backfill_UnknownCustomer_LeavesNameBlankAndDoesNotCount()
    {
        _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "C000000", DeliveryLocationName = null,
        });
        await _db.Context.SaveChangesAsync();

        var result = await Service(new FakeBusinessPartnerService()).ExecuteAsync();

        Assert.Equal(1, result.Scanned);
        Assert.Equal(0, result.Updated);
        var release = await _db.Context.SalesShipmentReleases.SingleAsync();
        Assert.True(string.IsNullOrEmpty(release.DeliveryLocationName));
    }
}
