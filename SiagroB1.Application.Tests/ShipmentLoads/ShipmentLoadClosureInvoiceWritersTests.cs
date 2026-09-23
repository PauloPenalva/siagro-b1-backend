using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): os caminhos do documento de saída que mudam a entrega de um item também
/// precisam desfazer uma Concluída. O caso perigoso é o cancelamento ou a exclusão de uma
/// devolução, que reabre a origem na mesma transação do hook da carga. Hoje os dois serviços
/// salvam entre a reabertura e o hook, e a leitura rastreada da Conferência no recálculo cobre
/// o dia em que esse save sair. Os testes travam o resultado, qualquer que seja a ordem dos flushes.
/// </summary>
public class ShipmentLoadClosureInvoiceWritersTests
{
    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesConfirmService Confirm(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(
                new UsageService(db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesCancelService Cancel(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private static SalesInvoicesDeleteService Delete(UnitOfWork db) =>
        new(db,
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

    /// <summary>
    /// Carga de 200 t "Concluída" no banco: a nota C (100 t, confirmada, entregue) e a nota A
    /// (100 t) já retornada, com a entrega fechada pelo retorno e uma devolução PENDENTE sobre
    /// ela. Uma devolução pendente não abate o faturado, então a carga segue Faturada nos números.
    /// <paramref name="isDischarged"/> liga a marca manual de Descarregada.
    /// </summary>
    private static async Task<(ShipmentLoad Load, SalesInvoice Return)> SeedPendingReturnAsync(
        UnitOfWork db, bool isDischarged = false)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000061",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 200m,
            InvoicedQuantity = 200m,
            Status = ShipmentLoadStatus.Completed,
            IsDischarged = isDischarged,
        };
        db.Context.ShipmentLoads.Add(load);

        var origin = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Returned);
        origin.ShipmentLoadKey = load.Key;
        origin.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        var originItem = SalesContractsAllocationTestSupport.NewItem(origin, contractKey: null, releaseKey: null, quantity: 100m);
        originItem.DeliveredQuantity = 100m;
        originItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        var other = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        other.ShipmentLoadKey = load.Key;
        var otherItem = SalesContractsAllocationTestSupport.NewItem(other, contractKey: null, releaseKey: null, quantity: 100m);
        otherItem.DeliveredQuantity = 100m;
        otherItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        var returnInvoice = SalesContractsAllocationTestSupport.NewInvoice(
            InvoiceStatus.Pending, SalesInvoiceType.Return, originKey: origin.Key);
        SalesContractsAllocationTestSupport.NewItem(
            returnInvoice, contractKey: null, releaseKey: null, quantity: 100m, originItemKey: originItem.Key);
        SalesInvoicesReturnWeightService.Apply(returnInvoice);

        db.Context.SalesInvoices.AddRange(origin, other, returnInvoice);
        await db.SaveChangesAsync();

        return (load, returnInvoice);
    }

    private static Task<ShipmentLoad> LoadAsync(UnitOfWork db) =>
        db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    /// <summary>A nota C: a Normal confirmada da carga.</summary>
    private static Task<SalesInvoice> ConfirmedNormalInvoiceAsync(UnitOfWork db) =>
        db.Context.SalesInvoices
            .SingleAsync(i => i.InvoiceType == SalesInvoiceType.Normal && i.InvoiceStatus == InvoiceStatus.Confirmed);

    /// <summary>
    /// A transição de situação deixa exatamente uma linha no log da carga, assinada por quem agiu,
    /// e nenhum movimento: o saldo não muda (Pendente continua consumindo).
    /// </summary>
    private static async Task AssertSingleStatusLogAndNoMovementAsync(
        UnitOfWork db, ShipmentLoadStatus from, ShipmentLoadStatus to)
    {
        var log = Assert.Single(await db.Context.ShipmentLoadsChangeLogs.AsNoTracking().ToListAsync());
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal(ShipmentLoadChangeLogFields.DescribeStatus(from), log.OldValue);
        Assert.Equal(ShipmentLoadChangeLogFields.DescribeStatus(to), log.NewValue);
        Assert.Equal("tester", log.ChangedBy);

        Assert.Empty(await db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Cancelling_a_pending_return_reopens_the_origin_and_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        var (_, returnInvoice) = await SeedPendingReturnAsync(db);

        await Cancel(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
    }

    [Fact]
    public async Task Deleting_a_pending_return_reopens_the_origin_and_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        var (_, returnInvoice) = await SeedPendingReturnAsync(db);

        await Delete(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
    }

    /// <summary>
    /// Estornar a confirmação devolve a nota a Pendente, e uma nota Pendente ainda não entrou
    /// na Conferência: a carga deixa de ser Concluída. Volta a Faturada, e não a Faturada
    /// Parcial, porque Pendente continua consumindo o saldo.
    /// </summary>
    [Fact]
    public async Task Reversing_the_confirmation_of_a_load_invoice_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedPendingReturnAsync(db);
        var other = await ConfirmedNormalInvoiceAsync(db);

        await Reverse(db).ExecuteAsync(other.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
        await AssertSingleStatusLogAndNoMovementAsync(
            db, ShipmentLoadStatus.Completed, ShipmentLoadStatus.Invoiced);
    }

    /// <summary>
    /// A marca manual sobrevive ao estorno: a carga continua no ramo Faturada, então sai da
    /// Concluída para a Descarregada, que é o que o usuário tinha afirmado.
    /// </summary>
    [Fact]
    public async Task Reversing_the_confirmation_of_a_discharged_load_invoice_returns_to_discharged()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedPendingReturnAsync(db, isDischarged: true);
        var other = await ConfirmedNormalInvoiceAsync(db);

        await Reverse(db).ExecuteAsync(other.Key, "tester");

        var load = await LoadAsync(db);
        Assert.Equal(ShipmentLoadStatus.Discharged, load.Status);
        Assert.True(load.IsDischarged);
        await AssertSingleStatusLogAndNoMovementAsync(
            db, ShipmentLoadStatus.Completed, ShipmentLoadStatus.Discharged);
    }

    /// <summary>
    /// O estorno não reabre os itens: a Conferência da nota continua encerrada enquanto ela está
    /// Pendente. Confirmar de novo precisa, então, devolver a carga a Concluída; sem o recálculo
    /// na confirmação da nota Normal, ela ficaria Faturada para sempre com tudo conferido.
    /// </summary>
    [Fact]
    public async Task Reversing_then_reconfirming_a_load_invoice_restores_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedPendingReturnAsync(db);
        var other = await ConfirmedNormalInvoiceAsync(db);

        await Reverse(db).ExecuteAsync(other.Key, "tester");
        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);

        await Confirm(db).ExecuteAsync(other.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Completed, (await LoadAsync(db)).Status);
    }
}
