using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadAvailableQuantityTests
{
    [Theory]
    [InlineData(30000, 29800, 200, 0)]      // NF menor: a divergência fecha a carga
    [InlineData(30000, 30150, -150, 0)]     // NF maior: divergência negativa
    [InlineData(30000, 29800, 0, 200)]      // sem divergência registrada sobra saldo
    public void Divergence_is_the_sixth_term(decimal total, decimal invoiced, decimal divergence, decimal expected) =>
        Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, 0, 0, 0, divergence));

    [Fact]
    public void AvailableQuantity_uses_the_persisted_divergence()
    {
        var load = new ShipmentLoad
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 30000,
            InvoicedQuantity = 29800,
            InvoiceDivergenceQuantity = 200,
        };

        Assert.Equal(0m, load.AvailableQuantity);
    }
}
