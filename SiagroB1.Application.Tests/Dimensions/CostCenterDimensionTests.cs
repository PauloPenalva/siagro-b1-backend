using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Dimensions;

public class CostCenterDimensionTests
{
    private static CostCenterService CreateService(SiagroB1.Infra.IUnitOfWork db) =>
        new(db, NullLogger<CostCenterService>.Instance);

    [Fact]
    public async Task Create_rejects_code_used_by_rule()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.DistributionRules.Add(new DistributionRule { Code = "R01", Name = "Rateio", DimensionNumber = 1 });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            CreateService(db).CreateAsync(new CostCenterModel { Code = "R01", Name = "X", DimensionNumber = 1 }));

        Assert.Equal("O código R01 já é uma regra de distribuição.", ex.Message);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    public async Task Create_rejects_dimension_out_of_range(int dimension)
    {
        var db = TestDb.CreateUnitOfWork();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            CreateService(db).CreateAsync(new CostCenterModel { Code = "CC01", Name = "X", DimensionNumber = dimension }));

        Assert.Equal("Dimensão inválida.", ex.Message);
    }

    [Fact]
    public async Task Create_normalizes_code()
    {
        var db = TestDb.CreateUnitOfWork();

        var created = await CreateService(db).CreateAsync(new CostCenterModel { Code = " cc01 ", Name = "X", DimensionNumber = 2 });

        Assert.Equal("CC01", created.Code);
        var saved = await db.Context.CostCenters.AsNoTracking().SingleAsync();
        Assert.Equal("CC01", saved.Code);
        Assert.Equal(2, saved.DimensionNumber);
    }

    [Fact]
    public async Task Update_allows_changing_dimension()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = CreateService(db);
        await service.CreateAsync(new CostCenterModel { Code = "CC01", Name = "X", DimensionNumber = 1 });

        var updated = await service.UpdateAsync("CC01", new CostCenterModel { Code = "CC01", Name = "X", DimensionNumber = 3 });

        Assert.Equal(3, updated!.DimensionNumber);
    }
}
