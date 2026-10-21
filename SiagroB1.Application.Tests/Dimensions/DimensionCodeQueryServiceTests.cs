using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Dimensions;

public class DimensionCodeQueryServiceTests
{
    [Fact]
    public async Task DimensionCodes_lists_centers_and_rules_of_each_dimension()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.CostCenters.Add(new CostCenter { Code = "CC01", Name = "Centro", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "VEI1", Name = "Caminhão", DimensionNumber = 2, Inactive = true });
        db.Context.DistributionRules.Add(new DistributionRule { Code = "R01", Name = "Rateio", DimensionNumber = 2 });
        await db.SaveChangesAsync();

        var all = await new DimensionCodeQueryService(db).QueryAll().OrderBy(x => x.Code).ToListAsync();

        Assert.Equal(["CC01", "R01", "VEI1"], all.Select(x => x.Code));
        Assert.Equal(["CostCenter", "Rule", "CostCenter"], all.Select(x => x.Kind));
        Assert.Equal([1, 2, 2], all.Select(x => x.DimensionNumber));
        Assert.Equal([false, false, true], all.Select(x => x.Inactive));
    }
}
