using SiagroB1.Application.Services.UserTruckScales;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.UserTruckScales;

public class UserTruckScalesServicesTests
{
    private static async Task<IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.UserTruckScales.Add(new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS01",
            Purpose = WeighingScalePurpose.Opening
        });

        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task A_second_scale_for_the_same_purpose_is_accepted()
    {
        var db = await SeedAsync();

        await new UserTruckScalesCreateService(db).ExecuteAsync(new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS02",
            Purpose = WeighingScalePurpose.Opening
        });

        Assert.Equal(2, db.Context.UserTruckScales.Count(x => x.Purpose == WeighingScalePurpose.Opening));
    }

    [Fact]
    public async Task The_same_scale_and_purpose_twice_is_refused()
    {
        var db = await SeedAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => new UserTruckScalesCreateService(db).ExecuteAsync(new UserTruckScale
            {
                Username = "joao",
                TruckScaleCode = "TS01",
                Purpose = WeighingScalePurpose.Opening
            }));

        Assert.Contains("Esta balança já está configurada", error.Message);
    }

    [Fact]
    public async Task Changing_a_row_to_another_scale_of_the_same_purpose_is_accepted()
    {
        var db = await SeedAsync();

        var other = new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS02",
            Purpose = WeighingScalePurpose.Closing
        };

        db.Context.UserTruckScales.Add(other);
        await db.SaveChangesAsync();

        // Como no PATCH do OData: a entidade chega rastreada, já com o Delta aplicado.
        other.Purpose = WeighingScalePurpose.Opening;

        await new UserTruckScalesUpdateService(db).ExecuteAsync(other.Id, other);

        Assert.Equal(2, db.Context.UserTruckScales.Count(x => x.Purpose == WeighingScalePurpose.Opening));
    }

    [Fact]
    public async Task Changing_a_row_into_an_existing_scale_and_purpose_is_refused()
    {
        var db = await SeedAsync();

        var other = new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS01",
            Purpose = WeighingScalePurpose.Closing
        };

        db.Context.UserTruckScales.Add(other);
        await db.SaveChangesAsync();

        other.Purpose = WeighingScalePurpose.Opening;

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => new UserTruckScalesUpdateService(db).ExecuteAsync(other.Id, other));

        Assert.Contains("Esta balança já está configurada", error.Message);
    }
}
