using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.FreightOrders;

/// <summary>Modelo EF da ordem de frete e do vínculo com o CT-e.</summary>
public class FreightOrderModelTests
{
    [Fact]
    public void FreightPriceCalculator_PerTon_UsesTonnes()
        => Assert.Equal(1600m, FreightPriceCalculator.Calculate(FreightCalculationMethod.PerTon, 50m, 32_000m));

    [Fact]
    public void Status_enum_values_are_stable()
        => Assert.Equal(new[] { "Open", "InExecution", "Completed", "Cancelled" }, Enum.GetNames<FreightOrderStatus>());

    [Fact]
    public void Model_has_unique_branch_code_index_and_restrict_fk()
    {
        var db = TestDb.CreateUnitOfWork();
        var order = db.Context.Model.FindEntityType(typeof(FreightOrder))!;
        Assert.Contains(order.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "BranchCode", "Code" }));

        var cte = db.Context.Model.FindEntityType(typeof(TransportDocument))!;
        var fk = cte.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(FreightOrder));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public async Task FreightOrder_RoundTrips_WithCteLink()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var order = new FreightOrder
        {
            BranchCode = "12345678000190", Code = "OF-1", DocNumberKey = Guid.NewGuid(),
            OrderDate = new DateTime(2026, 10, 24), TakerCardCode = "C1",
            StartMunicipalityCode = "3509502", StartState = "SP", EndMunicipalityCode = "4106902", EndState = "PR",
            ItemCode = "SOJA", ItemName = "Soja", ContractedWeightKg = 100_000m,
            PriceMethod = FreightCalculationMethod.PerTon, UnitPrice = 50m, ValidFrom = new DateTime(2026, 10, 24),
            CreatedBy = "tester", CreatedAt = new DateTime(2026, 10, 24),
        };
        db.Context.Set<FreightOrder>().Add(order);
        var cte = new TransportDocument { BranchCode = "12345678000190", FreightOrderKey = order.Key };
        db.Context.Set<TransportDocument>().Add(cte);
        await db.Context.SaveChangesAsync();

        var db2 = TestDb.CreateUnitOfWork(name);
        var loadedCte = await db2.Context.Set<TransportDocument>().SingleAsync(x => x.Key == cte.Key);
        Assert.Equal(order.Key, loadedCte.FreightOrderKey);
        var loaded = await db2.Context.Set<FreightOrder>().SingleAsync(x => x.Key == order.Key);
        Assert.Equal(FreightOrderStatus.Open, loaded.Status);
        Assert.Equal(0m, loaded.TransportedWeightKg);
    }
}
