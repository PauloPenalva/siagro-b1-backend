using System.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class SalesContractDeliveryLocationModelTests
{
    [Fact]
    public void SalesContractDeliveryLocation_IsMappedToExpectedTableAndColumns()
    {
        // Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(SalesContractDeliveryLocation));
        Assert.NotNull(entityType);
        Assert.Equal("SALES_CONTRACTS_DELIVERY_LOCATIONS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", props);
        Assert.Contains("SalesContractKey", props);
        Assert.Contains("CardCode", props);
        Assert.Contains("CardName", props);
    }
}
