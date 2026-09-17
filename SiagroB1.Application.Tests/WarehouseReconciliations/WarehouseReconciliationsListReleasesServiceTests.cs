namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsListReleasesServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    [Fact]
    public async Task Lists_lines_with_contract_producer_and_generated_codes()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, DateTime.Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(19_000m, DateTime.Today, (release, 1_000m));

        var rows = await new Services.WarehouseReconciliations.WarehouseReconciliationsListReleasesService(_ctx.Db)
            .ExecuteAsync(r.Key);

        var row = Assert.Single(rows);
        Assert.Equal(1_000m, row.Quantity);
        Assert.StartsWith("PC-", row.PurchaseContractCode);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, row.CardCode);
        Assert.False(string.IsNullOrEmpty(row.PurchaseTransactionCode));
        Assert.False(string.IsNullOrEmpty(row.LossTransactionCode));
    }
}
