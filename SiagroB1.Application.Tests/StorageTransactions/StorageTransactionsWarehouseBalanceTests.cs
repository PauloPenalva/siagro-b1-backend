using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Saldo do armazém usado na confirmação de um romaneio de embarque — a trava
/// "quantidade embarcada superior ao saldo disponível".
/// </summary>
/// <remarks>
/// O saldo somava apenas transações <c>Confirmed</c>, e o efeito era invisível e grave: ao ser
/// FATURADO o romaneio de saída passava a <c>Invoiced</c>, saía da conta, e o volume que ele
/// tinha retirado voltava a aparecer como disponível. Faturar não devolve grão ao armazém.
/// <para>
/// As consultas de saldo por ENDEREÇO (<c>StorageAddressesGetBalanceService</c> e as demais)
/// sempre contaram <c>Confirmed</c> + <c>Invoiced</c> — esta era a única fora do padrão, e a
/// divergência entre as duas fazia o mesmo armazém mostrar números diferentes conforme a tela.
/// </para>
/// </remarks>
public class StorageTransactionsWarehouseBalanceTests
{
    private const string Warehouse = "ARM01";
    private const string Item = "SOJA";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsConfirmedService Service() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private StorageTransaction Add(
        StorageTransactionType type,
        StorageTransactionsStatus status,
        decimal weight,
        string code)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            WarehouseCode = Warehouse,
            BranchCode = "01",
            TransactionType = type,
            TransactionStatus = status,
            GrossWeight = weight,
            NetWeight = weight,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Uma compra FATURADA continua no armazém: o grão entrou e não saiu porque a nota foi
    /// emitida. Antes ela sumia do saldo e um embarque legítimo era recusado.
    /// </summary>
    [Fact]
    public async Task An_invoiced_purchase_still_counts_as_stock()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Invoiced, 1_000m, "P1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 500m, "S1");
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(shipment, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, shipment.TransactionStatus);
    }

    /// <summary>
    /// O caso que dava o furo: um embarque FATURADO tem de continuar debitando. Com 1.000
    /// comprados e 800 já embarcados e faturados, sobram 200 — e um embarque de 500 tem de ser
    /// recusado. Antes o faturamento devolvia os 800 ao saldo e ele passava.
    /// </summary>
    [Fact]
    public async Task An_invoiced_shipment_still_debits_the_warehouse()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Invoiced, 800m, "S1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 500m, "S2");
        await _db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(shipment, "tester"));

        Assert.Equal(StorageTransactionsStatus.Pending, shipment.TransactionStatus);
    }

    /// <summary>
    /// E o que cabe no saldo continua passando: 1.000 comprados, 800 embarcados e faturados,
    /// um embarque de 200 fecha a conta exatamente.
    /// </summary>
    [Fact]
    public async Task A_shipment_that_fits_the_remaining_balance_is_confirmed()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Invoiced, 800m, "S1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 200m, "S2");
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(shipment, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, shipment.TransactionStatus);
    }

    /// <summary>
    /// Romaneio DEVOLVIDO sai da conta e devolve o volume ao armazém de origem — é o mecanismo
    /// que faz o retorno de documento de saída re-creditar a origem sozinho. Precisa continuar
    /// valendo depois da mudança.
    /// </summary>
    [Fact]
    public async Task A_returned_shipment_gives_the_volume_back()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Returned, 800m, "S1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 900m, "S2");
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(shipment, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, shipment.TransactionStatus);
    }

    /// <summary>
    /// Transação CANCELADA nunca conta, em nenhum dos dois status válidos.
    /// </summary>
    [Fact]
    public async Task A_cancelled_purchase_does_not_count_as_stock()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Cancelled, 1_000m, "P1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 500m, "S1");
        await _db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(shipment, "tester"));

        Assert.Equal(StorageTransactionsStatus.Pending, shipment.TransactionStatus);
    }
}
