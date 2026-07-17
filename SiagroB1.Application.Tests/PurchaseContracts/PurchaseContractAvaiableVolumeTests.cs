using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractAvaiableVolumeTests
{
    [Fact]
    public void AvaiableVolume_DerivesFromAllocatedVolume_WithoutLoadingAllocations()
    {
        // Prova que a dependência de navegação sumiu: sem nenhuma alocação
        // carregada, o saldo vem de TotalVolume − AllocatedVolume.
        var contract = new PurchaseContract
        {
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 5000m,
            AllocatedVolume = 1200m,
        };

        Assert.Empty(contract.Allocations);
        Assert.Equal(3800m, contract.AvaiableVolume);
    }

    [Fact]
    public void AvaiableVolume_NegativeAllocated_IncreasesAvailable()
    {
        // Devolução (Volume negativo) reduz o alocado e aumenta o disponível.
        var contract = new PurchaseContract
        {
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 5000m,
            AllocatedVolume = -100m,
        };

        Assert.Equal(5100m, contract.AvaiableVolume);
    }
}
