using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalLockTests
{
    private const string Message =
        "A carga CG000001 tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa.";

    private static ShipmentLoad Locked() => new()
    {
        Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        TotalQuantity = 30_000m, InvoicedQuantity = 30_000m, Status = ShipmentLoadStatus.RefusalPending,
        CarrierCardCode = "T-001", LoadType = ShipmentLoadType.Normal,
    };

    [Fact]
    public void Message_is_the_spec_text()
    {
        Assert.Equal(Message, ShipmentLoadRefusalRules.PendingMessage("CG000001"));
    }

    [Fact]
    public async Task Billing_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Locked();
        db.Context.ShipmentLoads.Add(load);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new ShipmentLoadsBillingGuardService(db.Context).EnsureCanBillAsync(load.Key, 1m, "T-001"));

        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Discharge_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(Locked()));
        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Transshipment_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(Locked()));
        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Change_log_describes_the_pending_refusal_in_portuguese()
    {
        Assert.Equal("Recusa aguardando NF-e", ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.RefusalPending));
    }

    [Fact]
    public void Other_statuses_pass()
    {
        var load = Locked();
        load.Status = ShipmentLoadStatus.Invoiced;

        ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);
    }
}
