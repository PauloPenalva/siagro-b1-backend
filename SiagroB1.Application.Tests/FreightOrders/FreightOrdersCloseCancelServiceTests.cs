using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.FreightOrders;

public class FreightOrdersCloseCancelServiceTests
{
    private static async Task<(SiagroB1.Infra.UnitOfWork Db, FreightOrder Order)> Seed(
        FreightOrderStatus status = FreightOrderStatus.Open, bool authorizedCte = false)
    {
        var db = TestDb.CreateUnitOfWork();
        var o = new FreightOrder
        {
            BranchCode = "01", Code = "OF-1", TakerCardCode = "C1", StartMunicipalityCode = "3550308", StartState = "SP",
            EndMunicipalityCode = "4106902", EndState = "PR", ItemCode = "SOJA", ItemName = "SOJA", CreatedBy = "t",
            Status = status,
        };
        db.Context.FreightOrders.Add(o);
        if (authorizedCte)
            db.Context.TransportDocuments.Add(new TransportDocument { BranchCode = "01", FreightOrderKey = o.Key, Status = CteStatus.Authorized });
        await db.SaveChangesAsync();
        return (db, o);
    }

    [Fact]
    public async Task Close_SetsCompleted_ClosedManually()
    {
        var (db, o) = await Seed(FreightOrderStatus.InExecution);
        await new FreightOrdersCloseService(db, TestConfiguration.Erp("STANDALONE")).CloseAsync(o.Key, "u");
        Assert.Equal(FreightOrderStatus.Completed, o.Status);
        Assert.True(o.ClosedManually);
    }

    [Fact]
    public async Task Close_Cancelled_Rejected()
    {
        var (db, o) = await Seed(FreightOrderStatus.Cancelled);
        await Assert.ThrowsAsync<DefaultException>(() =>
            new FreightOrdersCloseService(db, TestConfiguration.Erp("STANDALONE")).CloseAsync(o.Key, "u"));
    }

    [Fact]
    public async Task Cancel_SetsCancelled()
    {
        var (db, o) = await Seed();
        await new FreightOrdersCancelService(db, TestConfiguration.Erp("STANDALONE")).CancelAsync(o.Key, "u");
        Assert.Equal(FreightOrderStatus.Cancelled, o.Status);
    }

    [Fact]
    public async Task Cancel_WithAuthorizedCte_Rejected()
    {
        var (db, o) = await Seed(FreightOrderStatus.InExecution, authorizedCte: true);
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new FreightOrdersCancelService(db, TestConfiguration.Erp("STANDALONE")).CancelAsync(o.Key, "u"));
        Assert.Equal("A ordem de frete possui CT-e autorizado e não pode ser cancelada.", ex.Message);
    }
}
