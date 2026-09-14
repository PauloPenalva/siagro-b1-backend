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

    private StorageTransaction AddDated(
        StorageTransactionType type, decimal weight, string code, DateTime? date)
    {
        var transaction = Add(type, StorageTransactionsStatus.Confirmed, weight, code);
        transaction.TransactionDate = date;
        return transaction;
    }

    /// <summary>
    /// Sobra de armazém (14) credita e Perda de armazém (13) debita o saldo do armazém — é o
    /// efeito da Conferência de Saldo, sem contrato e sem lote.
    /// </summary>
    [Fact]
    public async Task Warehouse_gain_adds_and_warehouse_loss_subtracts()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseGain, StorageTransactionsStatus.Confirmed, 50m, "G1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed, 200m, "L1");
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item);

        Assert.Equal(850m, balance);
    }

    [Fact]
    public async Task Cancelled_warehouse_loss_does_not_count()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Cancelled, 200m, "L1");
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item);

        Assert.Equal(1_000m, balance);
    }

    /// <summary>
    /// "Saldo até a data": o que foi lançado DEPOIS da data de referência fica fora; a própria
    /// data entra inteira, inclusive com hora (o corte é o início do dia seguinte).
    /// </summary>
    [Fact]
    public async Task Up_to_date_excludes_later_transactions_and_includes_the_whole_day()
    {
        var reference = new DateTime(2026, 9, 10);
        AddDated(StorageTransactionType.Purchase, 1_000m, "P1", reference.AddDays(-5));
        AddDated(StorageTransactionType.Purchase, 300m, "P2", reference.AddHours(23).AddMinutes(59));
        AddDated(StorageTransactionType.SalesShipment, 400m, "S1", reference.AddDays(1));
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item, reference);

        Assert.Equal(1_300m, balance);
    }

    /// <summary>
    /// Romaneio legado sem data conta no "até a data": sem isso o saldo "até hoje" divergiria
    /// do saldo atual e a Conferência apuraria uma diferença fantasma.
    /// </summary>
    [Fact]
    public async Task Up_to_date_counts_transactions_without_date()
    {
        AddDated(StorageTransactionType.Purchase, 1_000m, "P1", null);
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item, new DateTime(2026, 9, 10));

        Assert.Equal(1_000m, balance);
    }

    /// <summary>
    /// A Perda baixa o que a Expedição pode embarcar: 1.000 comprados, 300 perdidos, um
    /// embarque de 800 tem de ser recusado.
    /// </summary>
    [Fact]
    public async Task A_shipment_above_the_balance_after_a_loss_is_refused()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed, 300m, "L1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 800m, "S1");
        await _db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(shipment, "tester"));

        Assert.Equal(StorageTransactionsStatus.Pending, shipment.TransactionStatus);
    }
}
