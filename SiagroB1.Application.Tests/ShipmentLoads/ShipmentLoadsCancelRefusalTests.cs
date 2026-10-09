using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>"Cancelar recusa" e a trava da devolução avulsa (spec 2026-10-09 §5.4).</summary>
public class ShipmentLoadsCancelRefusalTests
{
    private static async Task<(NfeLoadRefusalScenario S, List<Guid> Returns)> RefuseAsync(int documents = 2)
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, 10_000m)).ToList(),
                RefusalDestination.Rebilling, null, "Recusado"),
            "tester");
        var returns = await s.Db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).Select(i => i.Key).ToListAsync();
        s.Db.Context.ChangeTracker.Clear();
        return (s, returns);
    }

    [Fact]
    public async Task Cancels_pending_and_rejected_returns_and_unlocks_the_load()
    {
        var (s, returns) = await RefuseAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[1])).NfeStatus = NfeStatus.Rejected;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester");

        Assert.All(await s.Db.Context.SalesInvoices.AsNoTracking().Where(i => returns.Contains(i.Key)).ToListAsync(),
            r => Assert.Equal(InvoiceStatus.Cancelled, r.InvoiceStatus));
        var refusal = await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadRefusalStatus.Cancelled, refusal.Status);
        Assert.Equal("tester", refusal.CancelledBy);
        var load = await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Contains(await s.Db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync(),
            m => m.MovementType == ShipmentLoadMovementType.RefusalCancelled);
    }

    [Theory]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Processing)]
    public async Task Refused_when_a_return_nfe_is_authorized_or_processing(NfeStatus status)
    {
        var (s, returns) = await RefuseAsync();
        var r = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0]);
        r.NfeStatus = status;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester"));

        Assert.Equal(
            $"A NF-e de entrada do documento {r.InvoiceNumber} já foi autorizada ou está em processamento: " +
            "cancele a NF-e ou aguarde o retorno antes de cancelar a recusa.",
            ex.Message);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Refused_without_a_pending_refusal()
    {
        // Review Focus 5: duplo clique / recusa já concluída.
        var (s, _) = await RefuseAsync();
        await NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester");
        s.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester"));

        Assert.Equal("A carga CG000001 não tem recusa aguardando NF-e.", ex.Message);
    }

    [Fact]
    public async Task Cancelling_a_linked_return_directly_is_refused()
    {
        var (s, returns) = await RefuseAsync(1);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            NfeLoadRefusalTestSeed.CancelService(s.Db).ExecuteAsync(returns[0], "tester"));

        Assert.Equal("Esta devolução pertence à recusa da carga CG000001: cancele a recusa na Montagem de Carga.", ex.Message);
    }

    [Fact]
    public async Task Deleting_a_linked_return_is_refused()
    {
        var (s, returns) = await RefuseAsync(1);
        var delete = new SalesInvoicesDeleteService(
            s.Db,
            new ShipmentLoadsBalanceHookService(s.Db.Context, new ShipmentLoadsMovementLogService(s.Db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => delete.ExecuteAsync(returns[0], "tester"));

        Assert.Equal("Esta devolução pertence à recusa da carga CG000001: cancele a recusa na Montagem de Carga.", ex.Message);
        s.Db.Context.ChangeTracker.Clear();
        Assert.True(await s.Db.Context.SalesInvoices.AsNoTracking().AnyAsync(i => i.Key == returns[0]));
    }

    [Fact]
    public async Task Cancelling_after_the_nfe_was_cancelled_by_sefaz_is_allowed()
    {
        // Review Focus 3: o 2b cancela a NF-e de uma devolução com a recusa ainda pendente.
        var (s, returns) = await RefuseAsync(2);
        var r = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0]);
        r.NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelService(s.Db).CancelAfterNfeAsync(returns[0], "tester");

        Assert.Equal(InvoiceStatus.Cancelled, (await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == returns[0])).InvoiceStatus);
    }
}
