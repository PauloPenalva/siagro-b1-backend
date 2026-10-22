using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Trucks;

public class TruckModelTests
{
    [Fact]
    public void New_truck_defaults_to_own_traction_active()
    {
        var truck = new Truck { Code = "ABC1D23" };

        Assert.Equal(TruckKind.Traction, truck.Kind);
        Assert.Equal(TruckOwnerType.Own, truck.OwnerType);
        Assert.False(truck.Inactive);
    }

    [Fact]
    public async Task Seeded_truck_without_kind_reads_traction()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Trucks.Add(new Truck { Code = "ABC1D23", Model = "Scania" });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var truck = await db.Context.Trucks.SingleAsync();

        Assert.Equal(TruckKind.Traction, truck.Kind);
        Assert.Null(truck.WheelType);
    }

    [Fact]
    public void New_driver_is_active_without_license()
    {
        var driver = new TruckDriver { Code = "52998224725", Name = "João", Document = "", Phone = "" };

        Assert.False(driver.Inactive);
        Assert.Null(driver.LicenseExpiresOn);
    }
}
