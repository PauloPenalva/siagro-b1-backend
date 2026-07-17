using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class ShipmentReleaseConcurrencyTests
{
    [Fact]
    public void RowVersion_IsMappedAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var prop = context.Model
            .FindEntityType(typeof(ShipmentRelease))!
            .FindProperty(nameof(ShipmentRelease.RowVersion));

        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, prop.ValueGenerated);
    }
}
