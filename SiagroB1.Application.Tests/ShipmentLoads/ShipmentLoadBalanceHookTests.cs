using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Ciclo de vida do saldo da carga através dos serviços do documento de saída. É onde o
/// requisito "documento recusado reabre o saldo da carga para novo faturamento" fecha.
///
/// A regra que organiza tudo: <b>desfazer no mesmo nível em que o efeito foi aplicado</b>. O
/// consumo nasce na CRIAÇÃO da nota, então quem o desfaz é cancelar ou excluir — nunca
/// estornar a confirmação.
/// </summary>
public class ShipmentLoadBalanceHookTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsBalanceHookService Hook() =>
        new(_db.Context, new ShipmentLoadsMovementLogService(_db.Context));

    private SalesInvoicesConfirmService Confirm() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesInvoicesUsageGuardService(new UsageService(_db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            Hook(),
            new FakeStringLocalizer<Resource>());

    private SalesInvoicesReverseConfirmService ReverseConfirm() =>
        new(_db,
            new SalesContractsAllocationDeleteForInvoiceService(_db),
            Hook(),
            new FakeStringLocalizer<Resource>());

    private SalesInvoicesCancelService Cancel() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(_db),
            Hook(),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private SalesInvoicesDeleteService Delete() =>
        new(_db, Hook(), NullLogger<SalesInvoicesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesContract _contract = null!;
    private Guid _originItemKey;

    /// <summary>Carga de 90t já faturada por inteiro, com a nota confirmada.</summary>
    private async Task<SalesInvoice> SeedFullyBilledAsync()
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 90_000m,
            InvoicedQuantity = 90_000m,
            Status = ShipmentLoadStatus.Invoiced,
        };

        _contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);

        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        invoice.ShipmentLoadKey = _load.Key;
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, _contract.Key, releaseKey: null, quantity: 90_000m);
        _originItemKey = item.Key!.Value;

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesContracts.Add(_contract);
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        return invoice;
    }

    private SalesInvoice SeedReturn(SalesInvoice origin, InvoiceStatus status, decimal quantity = 90_000m)
    {
        var ret = SalesContractsAllocationTestSupport.NewInvoice(
            status, SalesInvoiceType.Return, originKey: origin.Key);
        // A devolução aponta a MESMA carga da origem — é o que SalesInvoiceCopyFactory faz.
        ret.ShipmentLoadKey = origin.ShipmentLoadKey;
        // A linha do retorno tem de apontar a linha de origem: o confirm valida o saldo
        // devolvido contra ela.
        SalesContractsAllocationTestSupport.NewItem(
            ret, _contract.Key, releaseKey: null, quantity: quantity,
            originItemKey: _originItemKey);

        _db.Context.SalesInvoices.Add(ret);
        return ret;
    }

    private async Task<ShipmentLoad> ReloadAsync() =>
        await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Confirming_the_return_gives_the_load_balance_back()
    {
        var origin = await SeedFullyBilledAsync();
        var ret = SeedReturn(origin, InvoiceStatus.Pending);
        origin.InvoiceStatus = InvoiceStatus.Returned;
        await _db.SaveChangesAsync();

        await Confirm().ExecuteAsync(ret.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(90_000m, saved.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);

        var movement = await _db.Context.ShipmentLoadMovements
            .AsNoTracking().SingleAsync(m => m.MovementType == ShipmentLoadMovementType.Returned);
        Assert.Equal(90_000m, movement.Quantity);
        Assert.Equal(90_000m, movement.BalanceAfter);
    }

    [Fact]
    public async Task Reversing_the_return_consumes_the_balance_again()
    {
        var origin = await SeedFullyBilledAsync();
        var ret = SeedReturn(origin, InvoiceStatus.Pending);
        origin.InvoiceStatus = InvoiceStatus.Returned;
        await _db.SaveChangesAsync();

        await Confirm().ExecuteAsync(ret.Key, "tester");
        await ReverseConfirm().ExecuteAsync(ret.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(90_000m, saved.InvoicedQuantity);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);

        var movement = await _db.Context.ShipmentLoadMovements
            .AsNoTracking().SingleAsync(m => m.MovementType == ShipmentLoadMovementType.ReturnReversed);
        Assert.Equal(-90_000m, movement.Quantity);
    }

    [Fact]
    public async Task Reversing_a_NORMAL_invoice_leaves_the_load_balance_untouched()
    {
        // O não-gancho deliberado: Pending continua consumindo, porque o consumo nasceu na
        // criação da nota. Quem desfaz é cancelar ou excluir.
        var origin = await SeedFullyBilledAsync();

        await ReverseConfirm().ExecuteAsync(origin.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(90_000m, saved.InvoicedQuantity);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);
        Assert.Empty(await _db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Cancelling_the_invoice_gives_the_load_balance_back()
    {
        var origin = await SeedFullyBilledAsync();

        await Cancel().ExecuteAsync(origin.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);

        var movement = await _db.Context.ShipmentLoadMovements
            .AsNoTracking().SingleAsync(m => m.MovementType == ShipmentLoadMovementType.BillingCancelled);
        Assert.Equal(90_000m, movement.Quantity);
    }

    [Fact]
    public async Task Deleting_a_pending_invoice_gives_the_balance_back_and_the_history_survives()
    {
        var origin = await SeedFullyBilledAsync();
        var tracked = await _db.Context.SalesInvoices.SingleAsync(x => x.Key == origin.Key);
        tracked.InvoiceStatus = InvoiceStatus.Pending;
        tracked.InvoiceNumber = "000000123";
        await _db.SaveChangesAsync();

        await Delete().ExecuteAsync(origin.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);

        // A linha do histórico sobrevive à exclusão da nota: SalesInvoiceKey não tem FK, e o
        // InvoiceNumber desnormalizado é o que o usuário lê.
        var movement = await _db.Context.ShipmentLoadMovements
            .AsNoTracking().SingleAsync(m => m.MovementType == ShipmentLoadMovementType.BillingDeleted);
        Assert.Equal("000000123", movement.InvoiceNumber);
        Assert.Equal(origin.Key, movement.SalesInvoiceKey);
        Assert.Equal(0, await _db.Context.SalesInvoices.CountAsync(x => x.Key == origin.Key));
    }

    [Fact]
    public async Task A_legacy_invoice_with_no_load_never_touches_the_history()
    {
        // O gancho é chamado incondicionalmente nos serviços compartilhados: precisa ser no-op
        // para todo documento legado e avulso, que é a maioria absoluta da base.
        _contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        SalesContractsAllocationTestSupport.NewItem(
            invoice, _contract.Key, releaseKey: null, quantity: 100m);

        _db.Context.SalesContracts.Add(_contract);
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        await Cancel().ExecuteAsync(invoice.Key, "tester");

        Assert.Empty(await _db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_full_cycle_converges_with_no_drift()
    {
        var origin = await SeedFullyBilledAsync();
        var ret = SeedReturn(origin, InvoiceStatus.Pending);
        origin.InvoiceStatus = InvoiceStatus.Returned;
        await _db.SaveChangesAsync();

        await Confirm().ExecuteAsync(ret.Key, "tester");          // devolve
        await ReverseConfirm().ExecuteAsync(ret.Key, "tester");   // re-consome
        await Confirm().ExecuteAsync(ret.Key, "tester");          // devolve de novo

        var saved = await ReloadAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(90_000m, saved.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);

        // O histórico registra os três passos; o saldo não depende dele.
        Assert.Equal(3, await _db.Context.ShipmentLoadMovements.CountAsync());
    }

    [Fact]
    public async Task A_cancelled_load_is_never_reopened_by_a_hook()
    {
        var origin = await SeedFullyBilledAsync();
        var trackedLoad = await _db.Context.ShipmentLoads.SingleAsync();
        trackedLoad.Status = ShipmentLoadStatus.Cancelled;
        trackedLoad.InvoicedQuantity = decimal.Zero;
        await _db.SaveChangesAsync();

        await Cancel().ExecuteAsync(origin.Key, "tester");

        var saved = await ReloadAsync();
        Assert.Equal(ShipmentLoadStatus.Cancelled, saved.Status);
        Assert.Empty(await _db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());
    }
}
