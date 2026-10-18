using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.DocNumbers;

public class DocNumberResolveInheritedKeyTests
{
    private static readonly Guid AutoKey = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ManualKey = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid InactiveKey = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    private sealed class StubService : DocNumberSequenceService
    {
        public Dictionary<Guid, DocNumberSeries> Series { get; } = [];

        public StubService() : base(null!) { }

        public override Task<DocNumberSeries?> GetSeriesAsync(Guid key) =>
            Task.FromResult(Series.GetValueOrDefault(key));
    }

    private static StubService CreateStub()
    {
        var stub = new StubService();
        stub.Series[AutoKey] = new DocNumberSeries(AutoKey, TransactionCode.SalesInvoice, "Automático", false, false);
        stub.Series[ManualKey] = new DocNumberSeries(ManualKey, TransactionCode.SalesInvoice, "Manual", true, false);
        stub.Series[InactiveKey] = new DocNumberSeries(InactiveKey, TransactionCode.SalesInvoice, "Velha", false, true);
        return stub;
    }

    [Fact]
    public async Task ActiveAutomaticSeries_IsInherited() =>
        Assert.Equal(AutoKey, await CreateStub().ResolveInheritedKeyAsync(AutoKey));

    [Fact]
    public async Task ManualSeries_FallsBackToNull() =>
        Assert.Null(await CreateStub().ResolveInheritedKeyAsync(ManualKey));

    [Fact]
    public async Task InactiveSeries_FallsBackToNull() =>
        Assert.Null(await CreateStub().ResolveInheritedKeyAsync(InactiveKey));

    [Fact]
    public async Task UnknownSeries_FallsBackToNull() =>
        Assert.Null(await CreateStub().ResolveInheritedKeyAsync(Guid.NewGuid()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrEmptyKey_ReturnsNull(bool empty) =>
        Assert.Null(await CreateStub().ResolveInheritedKeyAsync(empty ? Guid.Empty : null));
}

public class ShipmentLoadsCreateDocNumberKeyParameterTests
{
    [Fact]
    public void AbsentOrExplicitNull_MeansDefaultSeries()
    {
        Assert.Null(SiagroB1.Web.Actions.ShipmentLoads.ShipmentLoadsCreateController.DocNumberKeyOf(
            new Microsoft.AspNetCore.OData.Formatter.ODataActionParameters()));
        Assert.Null(SiagroB1.Web.Actions.ShipmentLoads.ShipmentLoadsCreateController.DocNumberKeyOf(
            new Microsoft.AspNetCore.OData.Formatter.ODataActionParameters { ["DocNumberKey"] = null }));
    }

    [Fact]
    public void GuidValue_IsKept()
    {
        var key = Guid.NewGuid();
        Assert.Equal(key, SiagroB1.Web.Actions.ShipmentLoads.ShipmentLoadsCreateController.DocNumberKeyOf(
            new Microsoft.AspNetCore.OData.Formatter.ODataActionParameters { ["DocNumberKey"] = key }));
    }
}
