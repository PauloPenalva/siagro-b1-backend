using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Quebra de trajeto (GAC-1213) na recusa em dois tempos: a recusa fica Pendente guardando o tipo da quebra e o
/// romaneio 18 só nasce quando a última NF-e de entrada é autorizada (spec §4.4).
/// </summary>
public class ShipmentLoadsDeferredTransitLossTests
{
    private static async Task<(NfeLoadRefusalScenario S, List<Guid> Returns)> RefuseAsync(
        int documents, decimal quantity, TransitLossReason? reason = TransitLossReason.Accident,
        RefusalDestination destination = RefusalDestination.TransitLoss)
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, quantity)).ToList(),
                destination, null, "Carga tombou na BR-116", reason),
            "tester");

        var returns = await s.Db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceType == SalesInvoiceType.Return)
            .OrderBy(i => i.InvoiceNumber)
            .Select(i => i.Key)
            .ToListAsync();
        s.Db.Context.ChangeTracker.Clear();
        return (s, returns);
    }

    private static Task ConfirmAsync(NfeLoadRefusalScenario s, Guid returnKey) =>
        NfeLoadRefusalTestSeed.AuthorizeAndConfirmAsync(
            s.Db, returnKey, NfeLoadRefusalTestSeed.ConfirmService(s.Db, NfeLoadRefusalTestSeed.Complete(s.Db)));

    private static Task<ShipmentLoad> LoadAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    private static Task<ShipmentLoadRefusal> RefusalAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();

    private static Task<List<StorageTransaction>> TransitLossEntriesAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.StorageTransactions.AsNoTracking()
            .Where(t => t.TransactionType == StorageTransactionType.TransitLoss).ToListAsync();

    private static async Task CancelNfeAsync(NfeLoadRefusalScenario s, Guid returnKey)
    {
        var invoice = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returnKey);
        invoice.NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelService(s.Db, NfeLoadRefusalTestSeed.Complete(s.Db))
            .CancelAfterNfeAsync(returnKey, "tester");
    }

    [Fact]
    public async Task Registration_keeps_the_refusal_pending_with_the_reason_and_creates_no_entry()
    {
        var (s, _) = await RefuseAsync(2, 10_000m);

        var refusal = await RefusalAsync(s);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, refusal.Status);
        Assert.Equal(RefusalDestination.TransitLoss, refusal.Destination);
        Assert.Equal(TransitLossReason.Accident, refusal.TransitLossReason);
        Assert.Null(refusal.DestinationWarehouseCode);
        Assert.Equal(ShipmentLoadStatus.RefusalPending, (await LoadAsync(s)).Status);
        Assert.Empty(await TransitLossEntriesAsync(s));
    }

    [Fact]
    public async Task Reason_is_not_stored_for_other_destinations()
    {
        var (s, _) = await RefuseAsync(1, 10_000m, TransitLossReason.Theft, RefusalDestination.Rebilling);

        Assert.Null((await RefusalAsync(s)).TransitLossReason);
    }

    [Fact]
    public async Task Last_confirmed_return_creates_the_entry_with_the_stored_reason_and_closes_the_load()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m);

        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        Assert.Empty(await TransitLossEntriesAsync(s));
        await ConfirmAsync(s, returns[1]);

        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await RefusalAsync(s)).Status);
        var entry = Assert.Single(await TransitLossEntriesAsync(s));
        Assert.Equal(TransitLossReason.Accident, entry.TransitLossReason);
        Assert.Equal(20_000m, entry.NetWeight);
        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ReturnInvoiceKey);

        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.TransitLoss, load.Status);
        Assert.Equal(20_000m, load.TransitLossQuantity);
    }

    [Fact]
    public async Task Cancelled_return_leaves_the_loss_with_the_confirmed_volume_only()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m);
        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[1])).NfeStatus = NfeStatus.Authorized;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await CancelNfeAsync(s, returns[1]);

        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await RefusalAsync(s)).Status);
        var entry = Assert.Single(await TransitLossEntriesAsync(s));
        Assert.Equal(10_000m, entry.NetWeight);
        Assert.Equal(10_000m, (await LoadAsync(s)).TransitLossQuantity);
    }

    [Fact]
    public async Task All_returns_cancelled_cancels_the_refusal_without_any_entry()
    {
        var (s, returns) = await RefuseAsync(1, 10_000m);
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0])).NfeStatus = NfeStatus.Authorized;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await CancelNfeAsync(s, returns[0]);

        Assert.Equal(ShipmentLoadRefusalStatus.Cancelled, (await RefusalAsync(s)).Status);
        Assert.Empty(await TransitLossEntriesAsync(s));
        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Equal(decimal.Zero, load.TransitLossQuantity);
    }

    [Fact]
    public async Task Cancel_refusal_leaves_no_loss_trace()
    {
        var (s, _) = await RefuseAsync(2, 10_000m);

        await NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester");

        Assert.Equal(ShipmentLoadRefusalStatus.Cancelled, (await RefusalAsync(s)).Status);
        Assert.Empty(await TransitLossEntriesAsync(s));
        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Equal(decimal.Zero, load.TransitLossQuantity);
    }

    [Fact]
    public async Task Pending_refusal_function_returns_the_reason_as_enum_name()
    {
        var (s, _) = await RefuseAsync(1, 10_000m, TransitLossReason.Theft);

        var rows = await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key);

        var row = Assert.Single(rows);
        Assert.Equal("TransitLoss", row.Destination);
        Assert.Equal("Theft", row.TransitLossReason);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row));
        Assert.Equal("Theft", json.RootElement.GetProperty("TransitLossReason").GetString());
    }

    [Fact]
    public async Task Pending_refusal_function_returns_null_reason_for_other_destinations()
    {
        var (s, _) = await RefuseAsync(1, 10_000m, null, RefusalDestination.Rebilling);

        var row = Assert.Single(await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key));

        Assert.Null(row.TransitLossReason);
    }
}
