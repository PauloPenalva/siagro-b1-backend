using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightOrders;

public class FreightOrdersRecalculateServiceTests
{
    private static FreightOrder NewOrder(decimal contractedKg = 50_000m) => new()
    {
        BranchCode = "01", Code = "OF-1", TakerCardCode = "C1", StartMunicipalityCode = "3550308", StartState = "SP",
        EndMunicipalityCode = "4106902", EndState = "PR", ItemCode = "SOJA", ItemName = "SOJA", CreatedBy = "t",
        ContractedWeightKg = contractedKg,
    };

    private static TransportDocument NewCte(Guid orderKey, decimal qty, string unit = "01", CteStatus status = CteStatus.Authorized)
    {
        var cte = new TransportDocument { BranchCode = "01", FreightOrderKey = orderKey, Status = status };
        cte.Quantities.Add(new TransportDocumentQuantity { UnitCode = unit, MeasureType = "PESO BRUTO", Quantity = qty });
        return cte;
    }

    private static async Task<(UnitOfWork Db, FreightOrder Order, FreightOrdersRecalculateService Svc)> Arrange(
        Action<FreightOrder>? tweak = null, params Func<Guid, TransportDocument>[] ctes)
    {
        var db = TestDb.CreateUnitOfWork();
        var order = NewOrder();
        tweak?.Invoke(order);
        db.Context.FreightOrders.Add(order);
        foreach (var c in ctes) db.Context.TransportDocuments.Add(c(order.Key));
        await db.SaveChangesAsync();
        return (db, order, new FreightOrdersRecalculateService(db));
    }

    [Fact]
    public async Task NoAuthorizedCte_IsOpen()
    {
        var (_, order, svc) = await Arrange(o => o.Status = FreightOrderStatus.InExecution, k => NewCte(k, 10, status: CteStatus.Draft));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.Open, order.Status);
        Assert.Equal(0m, order.TransportedWeightKg);
    }

    [Fact]
    public async Task OneCte_20t_of_50t_IsInExecution_Transported20000()
    {
        var (_, order, svc) = await Arrange(null, k => NewCte(k, 20_000m));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.InExecution, order.Status);
        Assert.Equal(20_000m, order.TransportedWeightKg);
    }

    [Fact]
    public async Task CteInTonnes_Converts()
    {
        var (_, order, svc) = await Arrange(null, k => NewCte(k, 20m, "02"));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(20_000m, order.TransportedWeightKg);
    }

    [Fact]
    public async Task BalanceZero_IsCompleted()
    {
        var (_, order, svc) = await Arrange(null, k => NewCte(k, 50_000m));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.Completed, order.Status);
    }

    [Fact]
    public async Task CancelledCte_ReopensCompleted()
    {
        var (db, order, svc) = await Arrange(null, k => NewCte(k, 25_000m), k => NewCte(k, 25_000m));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.Completed, order.Status);

        db.Context.TransportDocuments.First(d => d.FreightOrderKey == order.Key).Status = CteStatus.Cancelled;
        await db.SaveChangesAsync();
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.InExecution, order.Status);
        Assert.Equal(25_000m, order.TransportedWeightKg);
    }

    [Fact]
    public async Task ClosedManually_StaysCompleted()
    {
        var (_, order, svc) = await Arrange(o => { o.ClosedManually = true; o.Status = FreightOrderStatus.Completed; },
            k => NewCte(k, 10_000m));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.Completed, order.Status);
        Assert.Equal(10_000m, order.TransportedWeightKg);
    }

    [Fact]
    public async Task Cancelled_Order_IsUntouched()
    {
        var (_, order, svc) = await Arrange(o => o.Status = FreightOrderStatus.Cancelled, k => NewCte(k, 10_000m));
        await svc.RecalculateAsync(order.Key);
        Assert.Equal(FreightOrderStatus.Cancelled, order.Status);
    }
}
