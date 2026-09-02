using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Fórmula do terceiro termo do saldo da carga: o volume devolvido a um armazém por recusa.
/// </summary>
/// <remarks>
/// O vínculo é <c>RefusedFromShipmentLoadKey</c>, e não <c>ShipmentLoadKey</c>. Confundir as
/// duas é o erro que estes testes existem para pegar: a devolução entraria no volume EMBARCADO
/// da carga (<c>ShipmentLoadsRecalculateTotalService</c>) e o saldo cresceria em vez de encolher.
/// </remarks>
public class ShipmentLoadsRecalculateReturnedServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoad NewLoad(string code = "CG000010")
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = code,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            BranchCode = "01",
            TotalQuantity = 40_000m,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction NewTransaction(
        StorageTransactionType type,
        StorageTransactionsStatus status,
        decimal grossWeight,
        Guid? refusedFromLoadKey = null,
        Guid? shipmentLoadKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"RM{Guid.NewGuid():N}"[..10],
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = status,
            RefusedFromShipmentLoadKey = refusedFromLoadKey,
            ShipmentLoadKey = shipmentLoadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private Task<decimal> ReturnedAsync(ShipmentLoad load) =>
        ShipmentLoadsRecalculateReturnedService.CalculateReturnedToWarehouseAsync(_db.Context, load.Key);

    [Fact]
    public async Task A_load_with_no_refusal_has_nothing_returned()
    {
        var load = NewLoad();
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await ReturnedAsync(load));
    }

    [Fact]
    public async Task Confirmed_refusal_returns_are_summed_by_gross_weight()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 10_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(25_000m, await ReturnedAsync(load));
    }

    /// <summary>
    /// Cancelado não conta, como em todo somatório do projeto — senão uma devolução desfeita
    /// seguiria retirando saldo da carga para sempre.
    /// </summary>
    [Fact]
    public async Task A_cancelled_refusal_return_does_not_count()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Cancelled, 9_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(15_000m, await ReturnedAsync(load));
    }

    [Fact]
    public async Task A_refusal_return_of_another_load_does_not_count()
    {
        var load = NewLoad();
        var otherLoad = NewLoad("CG000011");
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: otherLoad.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await ReturnedAsync(load));
    }

    /// <summary>
    /// O filtro de TIPO: uma transação de outro tipo que venha a carregar a coluna não pode
    /// abater o saldo da carga.
    /// </summary>
    [Fact]
    public async Task A_non_return_transaction_carrying_the_column_does_not_count()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipment,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await ReturnedAsync(load));
    }

    /// <summary>
    /// O inverso da armadilha: um romaneio MONTADO na carga (<c>ShipmentLoadKey</c>) não é uma
    /// devolução. Confundir as duas colunas faria o embarque abater o próprio saldo.
    /// </summary>
    [Fact]
    public async Task A_shipment_attached_to_the_load_is_not_a_refusal_return()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipment,
            StorageTransactionsStatus.Confirmed, 40_000m, shipmentLoadKey: load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await ReturnedAsync(load));
    }

    /// <summary>
    /// E a simetria que fecha a armadilha: a devolução NÃO entra no volume embarcado, senão o
    /// total da carga cresceria a cada recusa.
    /// </summary>
    [Fact]
    public async Task A_refusal_return_does_not_inflate_the_loads_total_quantity()
    {
        var load = NewLoad();
        NewTransaction(StorageTransactionType.SalesShipment,
            StorageTransactionsStatus.Confirmed, 40_000m, shipmentLoadKey: load.Key);
        NewTransaction(StorageTransactionType.SalesShipmentReturn,
            StorageTransactionsStatus.Confirmed, 15_000m, refusedFromLoadKey: load.Key);
        await _db.SaveChangesAsync();

        await ShipmentLoadsRecalculateTotalService.RecalculateAsync(_db.Context, load.Key);

        Assert.Equal(40_000m, load.TotalQuantity);
    }
}
