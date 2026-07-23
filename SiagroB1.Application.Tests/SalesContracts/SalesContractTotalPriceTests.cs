using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractTotalPriceTests
{
    private static SalesContract NewContract(params SalesContractPriceFixation[] fixations)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
        };

        foreach (var fixation in fixations)
            contract.PriceFixations.Add(fixation);

        return contract;
    }

    private static SalesContractPriceFixation Fixation(
        decimal volume, decimal price, PriceFixationStatus status) => new()
    {
        Key = Guid.NewGuid(),
        FixationVolume = volume,
        FixationPrice = price,
        Status = status,
    };

    [Fact]
    public void TotalPrice_CountsConfirmedOnly()
    {
        var contract = NewContract(
            Fixation(10_000m, 2m, PriceFixationStatus.Confirmed),
            Fixation(10_000m, 5m, PriceFixationStatus.InApproval));

        Assert.Equal(20_000m, contract.TotalPrice);
    }

    [Fact]
    public void TotalPrice_IgnoresCanceledAndRejected()
    {
        var contract = NewContract(
            Fixation(10_000m, 2m, PriceFixationStatus.Confirmed),
            Fixation(10_000m, 9m, PriceFixationStatus.Canceled),
            Fixation(10_000m, 7m, PriceFixationStatus.Rejected));

        Assert.Equal(20_000m, contract.TotalPrice);
    }

    [Fact]
    public void TotalPrice_NoConfirmedFixations_IsZero()
    {
        var contract = NewContract(Fixation(10_000m, 5m, PriceFixationStatus.InApproval));

        Assert.Equal(0m, contract.TotalPrice);
    }
}
