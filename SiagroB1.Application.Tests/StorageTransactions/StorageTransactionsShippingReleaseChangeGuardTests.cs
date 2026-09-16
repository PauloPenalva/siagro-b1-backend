using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Commons.Resources;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Romaneio nascido de uma troca de liberação (GAC-1177) — estorno 12/9 ou Expedição nova 7/8 —
/// não pode ser cancelado/estornado pelas telas genéricas de Romaneios. As duas colunas
/// (<see cref="StorageTransaction.ShippingReleaseChangeKey"/> e
/// <see cref="StorageTransaction.ReplacedByShippingReleaseChangeKey"/>) bloqueiam: quem desfaz
/// esses romaneios é o fluxo da troca, não o caminho avulso.
/// </summary>
public class StorageTransactionsShippingReleaseChangeGuardTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransaction NewTransaction(
        Guid? shippingReleaseChangeKey, Guid? replacedByShippingReleaseChangeKey)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = 1000m,
            NetWeight = 1000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            ShippingReleaseChangeKey = shippingReleaseChangeKey,
            ReplacedByShippingReleaseChangeKey = replacedByShippingReleaseChangeKey,
        };
        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private StorageTransactionsCancelService CancelService() => new(
        _db, new ShipmentReleasesRecalculateShippedService(_db.Context));

    private StorageTransactionsReverseService ReverseService() => new(
        _db,
        new StorageAddressesGetBalanceService(null!),
        new ShipmentReleasesRecalculateShippedService(_db.Context),
        new FakeStringLocalizer<Resource>());

    [Fact]
    public async Task Cancel_refuses_a_transaction_generated_by_a_release_change()
    {
        var transaction = NewTransaction(shippingReleaseChangeKey: Guid.NewGuid(), replacedByShippingReleaseChangeKey: null);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => CancelService().ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("troca de liberação", error.Message);
        Assert.Equal(
            StorageTransactionsStatus.Confirmed,
            (await _db.Context.StorageTransactions.SingleAsync()).TransactionStatus);
    }

    [Fact]
    public async Task Cancel_refuses_a_transaction_replaced_by_a_release_change()
    {
        var transaction = NewTransaction(shippingReleaseChangeKey: null, replacedByShippingReleaseChangeKey: Guid.NewGuid());
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => CancelService().ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("troca de liberação", error.Message);
    }

    [Fact]
    public async Task Reverse_refuses_a_transaction_generated_by_a_release_change()
    {
        var transaction = NewTransaction(shippingReleaseChangeKey: Guid.NewGuid(), replacedByShippingReleaseChangeKey: null);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("troca de liberação", error.Message);
    }

    [Fact]
    public async Task Reverse_refuses_a_transaction_replaced_by_a_release_change()
    {
        var transaction = NewTransaction(shippingReleaseChangeKey: null, replacedByShippingReleaseChangeKey: Guid.NewGuid());
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(transaction.Key, "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("troca de liberação", error.Message);
    }
}
