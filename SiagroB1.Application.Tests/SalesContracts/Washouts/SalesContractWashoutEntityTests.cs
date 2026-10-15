using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §3.1 e §4.1: fórmula invertida e saldos que descontam o lavado.</summary>
public class SalesContractWashoutEntityTests
{
    [Fact]
    public void Contract_above_market_charges_the_difference_plus_the_penalty()
    {
        // (100 − 90) × 1.000 = 10.000 + 500 de multa
        Assert.Equal(10_500m, SalesContractWashout.CalculateAmount(90m, 100m, 1_000m, 500m));
    }

    [Fact]
    public void Market_above_contract_charges_only_the_penalty()
    {
        Assert.Equal(500m, SalesContractWashout.CalculateAmount(110m, 100m, 1_000m, 500m));
    }

    [Fact]
    public void Unfixed_only_washout_charges_only_the_penalty()
    {
        Assert.Equal(300m, SalesContractWashout.CalculateAmount(90m, 0m, 0m, 300m));
    }

    [Fact]
    public void The_difference_is_rounded_to_two_decimals()
    {
        // 0,005 × 333,333 = 1,666665 → 1,67
        Assert.Equal(1.67m, SalesContractWashout.CalculateAmount(2.5m, 2.505m, 333.333m, 0m));
    }

    [Fact]
    public void Origin_is_appended_with_a_fixed_number()
    {
        Assert.Equal(8, (int)FinancialDocumentOrigin.SalesContractWashout);
        Assert.Equal(3, (int)SalesContractWashoutStatus.Reversed);
    }

    [Fact]
    public void Washed_out_volume_reduces_the_physical_balance()
    {
        var contract = SalesWashoutTestData.Contract(totalVolume: 100_000m);
        contract.AllocatedVolume = 20_000m;
        contract.WashedOutVolume = 30_000m;

        Assert.Equal(50_000m, contract.AvaiableVolume);
    }

    [Fact]
    public void Washed_out_volume_reduces_both_balances_to_release()
    {
        var contract = SalesWashoutTestData.Contract(totalVolume: 100_000m);
        contract.WashedOutVolume = 10_000m;
        contract.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 40_000m));

        Assert.Equal(50_000m, contract.TotalAvailableToRelease);
        Assert.Equal(50_000m, contract.TotalAvailableToReleaseWithoutProvisioning);
    }

    [Fact]
    public void Unfixed_washed_volume_is_no_longer_available_to_pricing()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined, totalVolume: 100_000m);
        contract.FixedVolume = 30_000m;
        contract.WashedOutUnfixedVolume = 20_000m;

        Assert.Equal(50_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public void Change_log_describes_code_volumes_amount_and_status()
    {
        var washout = new SalesContractWashout
        {
            Sequence = 2,
            FixedVolume = 1_000m,
            UnfixedVolume = 500m,
            Amount = 1_234.5m,
            Status = SalesContractWashoutStatus.InApproval,
        };

        Assert.Equal(
            "WO-2: 1.000,000 KG fixado + 500,000 KG não fixado, R$ 1.234,50 — Em aprovação",
            ContractChangeLogFields.DescribeWashout(washout, "KG"));
    }
}
