using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// GAC-1191 — Frete/Remoção do romaneio calculado a partir do valor unitário.
/// </summary>
/// <remarks>
/// A Mhagro lança o valor da viagem (Fixo); a Yokotobi cobra a remoção por saco de 60 kg, inclusive
/// no trigo (Saco). <c>FreightPrice</c> continua sendo o TOTAL — é o que a fatura de armazenagem e o
/// relatório de romaneios leem —, mas deixa de ser digitado: o backend o deriva do método e do
/// unitário a cada gravação.
/// </remarks>
public class StorageTransactionFreightPriceTests
{
    private const string CardCode = "C0001";
    private const string ItemCode = "TRIGO";
    private const string WarehouseCode = "ARM01";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>
    /// Valores conferidos à mão. Saco = peso bruto (KG) / 60 × unitário, arredondado em 2 casas:
    /// 30.000 kg = 500 sacas; 1.000 kg = 16,666... sacas × 2,35 = 39,1666... → 39,17.
    /// </summary>
    [Theory]
    [InlineData(FreightCalculationMethod.Fixed, 1500.00, 30_000, 1500.00)]
    [InlineData(FreightCalculationMethod.Fixed, 800.00, 0, 800.00)]
    [InlineData(FreightCalculationMethod.PerBag, 3.00, 30_000, 1500.00)]
    [InlineData(FreightCalculationMethod.PerBag, 2.35, 1_000, 39.17)]
    [InlineData(FreightCalculationMethod.PerBag, 3.00, 0, 0.00)]
    public void Calculates_the_freight_total_from_the_method_and_the_unit_price(
        FreightCalculationMethod method, double unitPrice, double grossWeightKg, double expected)
    {
        var total = FreightPriceCalculator.Calculate(method, (decimal)unitPrice, (decimal)grossWeightKg);

        Assert.Equal((decimal)expected, total);
    }

    private StorageTransactionsCreateService CreateService() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Trigo" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsUpdateService UpdateService() =>
        new(_db.Context,
            _db,
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Trigo" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
            NullLogger<StorageTransactionsUpdateService>.Instance);

    private static StorageTransaction NewReceipt() => new()
    {
        Key = Guid.NewGuid(),
        DocNumberKey = Guid.NewGuid(),
        CardCode = CardCode,
        ItemCode = ItemCode,
        UnitOfMeasureCode = "KG",
        WarehouseCode = WarehouseCode,
        BranchCode = "01",
        TransactionType = StorageTransactionType.Receipt,
        TransactionStatus = StorageTransactionsStatus.Pending,
    };

    /// <summary>
    /// O total que vem da tela não é confiável (é só a prévia calculada no navegador): quem manda é
    /// o método e o unitário.
    /// </summary>
    [Fact]
    public async Task Creating_a_per_bag_transaction_derives_the_freight_total_from_the_gross_weight()
    {
        var transaction = NewReceipt();
        transaction.GrossWeight = 30_000m;
        transaction.FreightCalculationMethod = FreightCalculationMethod.PerBag;
        transaction.FreightUnitPrice = 3.00m;
        transaction.FreightPrice = 999m;

        await CreateService().ExecuteAsync(transaction, "tester");

        var saved = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(1500.00m, saved.FreightPrice);
    }

    /// <summary>
    /// Mesmo caminho do PATCH do controller: a entidade chega rastreada, já com o delta aplicado.
    /// </summary>
    [Fact]
    public async Task Updating_a_pending_transaction_recalculates_the_freight_total()
    {
        var transaction = NewReceipt();
        transaction.GrossWeight = 30_000m;
        transaction.FreightCalculationMethod = FreightCalculationMethod.Fixed;
        transaction.FreightUnitPrice = 800m;
        transaction.FreightPrice = 800m;
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        var tracked = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == transaction.Key);
        tracked.GrossWeight = 24_000m;
        tracked.FreightCalculationMethod = FreightCalculationMethod.PerBag;
        tracked.FreightUnitPrice = 2.50m;

        await UpdateService().ExecuteAsync(transaction.Key, tracked, "tester");

        var saved = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(1000.00m, saved.FreightPrice);
    }
}
