using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationPlannerTests
{
    private static readonly Guid L1 = Guid.NewGuid(), L2 = Guid.NewGuid(), T1 = Guid.NewGuid(), T2 = Guid.NewGuid();

    private static PlannerLine Line(Guid key, decimal available, int day = 1, PurchaseInvoiceType type = PurchaseInvoiceType.Normal) =>
        new(key, type, new DateTime(2026, 10, day), key.ToString(), available);

    private static PlannerTransaction Tx(Guid key, decimal open, int day = 1, StorageTransactionType type = StorageTransactionType.Purchase) =>
        new(key, type, new DateTime(2026, 10, day), key.ToString(), open);

    [Fact]
    public void One_invoice_two_trucks_fob_leftover_goes_to_last_truck()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 60_000m)], [Tx(T1, 29_820m, 1), Tx(T2, 29_900m, 2)]);

        Assert.Equal([new PlannerPair(L1, T1, 29_820m), new PlannerPair(L1, T2, 30_180m)], pairs);
    }

    [Fact]
    public void Two_invoices_one_truck_both_go_whole()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L2, 500m, 2), Line(L1, 30_000m, 1)], [Tx(T1, 30_200m)]);

        Assert.Equal([new PlannerPair(L1, T1, 30_000m), new PlannerPair(L2, T1, 500m)], pairs);
    }

    [Fact]
    public void Smaller_invoice_fills_trucks_in_date_order_and_stops()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 40_000m)], [Tx(T2, 30_000m, 2), Tx(T1, 30_000m, 1)]);

        Assert.Equal([new PlannerPair(L1, T1, 30_000m), new PlannerPair(L1, T2, 10_000m)], pairs);
    }

    [Fact]
    public void Return_invoice_only_reaches_return_and_discharge_loss()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest(
            [Line(L1, 300m, type: PurchaseInvoiceType.Return)],
            [Tx(T1, 1_000m, 1, StorageTransactionType.Purchase), Tx(T2, 300m, 2, StorageTransactionType.PurchaseDischargeLoss)]);

        Assert.Equal([new PlannerPair(L1, T2, 300m)], pairs);
    }

    [Fact]
    public void Already_covered_truck_only_receives_leftover_when_it_is_the_last()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 100m)], [Tx(T1, 0m, 1), Tx(T2, -50m, 2)]);

        Assert.Equal([new PlannerPair(L1, T2, 100m)], pairs);
    }

    [Fact]
    public void Line_without_available_or_matching_truck_suggests_nothing()
    {
        Assert.Empty(PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 0m)], [Tx(T1, 100m)]));
        Assert.Empty(PurchaseFiscalReconciliationPlanner.Suggest(
            [Line(L1, 10m)], [Tx(T1, 100m, type: StorageTransactionType.PurchaseReturn)]));
    }
}
