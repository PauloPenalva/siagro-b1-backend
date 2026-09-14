using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractWashoutEntityTests
{
    private static PurchaseContract Contract() => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 100_000m,
    };

    [Fact]
    public void Market_above_contract_charges_the_difference_on_the_fixed_volume_plus_the_penalty()
    {
        // (2,75 − 2,50) × 10.000 = 2.500 + 1.000 de multa
        Assert.Equal(3_500m, PurchaseContractWashout.CalculateAmount(2.75m, 2.50m, 10_000m, 1_000m));
    }

    [Fact]
    public void Market_below_contract_charges_only_the_penalty()
    {
        Assert.Equal(500m, PurchaseContractWashout.CalculateAmount(2.00m, 2.50m, 10_000m, 500m));
    }

    [Fact]
    public void Unfixed_only_washout_charges_only_the_penalty()
    {
        Assert.Equal(300m, PurchaseContractWashout.CalculateAmount(9m, 0m, 0m, 300m));
    }

    [Fact]
    public void The_difference_is_rounded_to_two_decimals()
    {
        // 0,005 × 333,333 = 1,666665 → 1,67
        Assert.Equal(1.67m, PurchaseContractWashout.CalculateAmount(2.505m, 2.5m, 333.333m, 0m));
    }

    [Fact]
    public void Washed_out_volume_reduces_the_physical_balance()
    {
        var contract = Contract();
        contract.AllocatedVolume = 20_000m;
        contract.WashedOutVolume = 30_000m;

        Assert.Equal(50_000m, contract.AvaiableVolume);
    }

    [Fact]
    public void Washed_out_volume_reduces_both_balances_to_release()
    {
        var contract = Contract();
        contract.WashedOutVolume = 10_000m;
        contract.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 40_000m,
            Status = ReleaseStatus.Actived,
        });

        Assert.Equal(50_000m, contract.TotalAvailableToRelease);
        Assert.Equal(50_000m, contract.TotalAvailableToReleaseWithoutProvisioning);
    }

    [Fact]
    public void Unfixed_washed_volume_is_no_longer_available_to_pricing()
    {
        var contract = Contract();
        contract.FixedVolume = 30_000m;
        contract.WashedOutUnfixedVolume = 20_000m;

        Assert.Equal(50_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public void Change_log_describes_code_volumes_amount_and_status()
    {
        var washout = new PurchaseContractWashout
        {
            Sequence = 2,
            FixedVolume = 1_000m,
            UnfixedVolume = 500m,
            Amount = 1_234.5m,
            Status = PurchaseContractWashoutStatus.InApproval,
        };

        Assert.Equal(
            "WO-2: 1.000,000 KG fixado + 500,000 KG não fixado, R$ 1.234,50 — Em aprovação",
            ContractChangeLogFields.DescribeWashout(washout, "KG"));
    }
}
