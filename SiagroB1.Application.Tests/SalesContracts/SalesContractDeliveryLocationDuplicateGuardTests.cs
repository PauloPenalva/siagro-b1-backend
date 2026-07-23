using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractDeliveryLocationDuplicateGuardTests
{
    private static SalesContractDeliveryLocation Loc(string card) => new() { CardCode = card };

    [Fact]
    public void HasDuplicate_WithRepeatedCardCode_ReturnsTrue()
    {
        var locations = new[] { Loc("C0001"), Loc("C0002"), Loc("C0001") };
        Assert.True(SalesContractsCreateService.HasDuplicateDeliveryLocation(locations));
    }

    [Fact]
    public void HasDuplicate_WithDistinctCardCodes_ReturnsFalse()
    {
        var locations = new[] { Loc("C0001"), Loc("C0002") };
        Assert.False(SalesContractsCreateService.HasDuplicateDeliveryLocation(locations));
    }

    [Fact]
    public void HasDuplicate_WithEmptyCollection_ReturnsFalse()
    {
        Assert.False(SalesContractsCreateService.HasDuplicateDeliveryLocation([]));
    }
}
