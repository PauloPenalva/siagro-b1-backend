using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Commons.Resources;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Um romaneio montado em carga não pode ser cancelado nem estornado pelo caminho genérico
/// do romaneio: durante o faturamento PARCIAL ele ainda está <c>Confirmed</c>, então o guard
/// de status deixaria passar e destruiria romaneio já faturado em parte. O guard é pela
/// presença da carga, não pelo status — que é derivado e oscila.
/// </summary>
public class StorageTransactionsShipmentLoadGuardTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransaction ShipmentInLoad(StorageTransactionsStatus status)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 30_000,
        };
        _db.Context.ShipmentLoads.Add(load);

        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = 30_000,
            NetWeight = 30_000,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = status,
            // Origem preenchida para o teste alcançar o guard novo, e não parar no guard
            // de "criado por outra transação", que vem antes no serviço.
            TransactionOrigin = TransactionCode.StorageTransaction,
            ShipmentLoadKey = load.Key,
        };
        _db.Context.StorageTransactions.Add(transaction);

        return transaction;
    }

    [Fact]
    public async Task Cancel_refuses_a_shipment_that_belongs_to_a_load()
    {
        // Confirmed de propósito: é o estado do romaneio durante o faturamento parcial,
        // exatamente o caso que um guard por status deixaria passar.
        var transaction = ShipmentInLoad(StorageTransactionsStatus.Confirmed);
        await _db.Context.SaveChangesAsync();

        var service = new StorageTransactionsCancelService(
            _db,
            new ShipmentReleasesRecalculateShippedService(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("CG000007", error.Message);
        Assert.Equal(
            StorageTransactionsStatus.Confirmed,
            (await _db.Context.StorageTransactions.SingleAsync()).TransactionStatus);
    }

    [Fact]
    public async Task Reverse_refuses_a_shipment_that_belongs_to_a_load()
    {
        var transaction = ShipmentInLoad(StorageTransactionsStatus.Confirmed);
        await _db.Context.SaveChangesAsync();

        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("CG000007", error.Message);
    }
}
