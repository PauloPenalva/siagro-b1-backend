using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateShippedService Service() => new(_db.Context);

    private async Task<ShipmentRelease> SeedReleaseAsync(
        decimal released, ReleaseOrigin origin = ReleaseOrigin.Standard)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = 999m, // valor errado, deve ser recalculado
            Status = ReleaseStatus.Actived,
            Origin = origin,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private StorageTransaction Tx(Guid releaseKey, StorageTransactionType type, decimal net,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = type,
        TransactionStatus = status,
        NetWeight = net,
        ShipmentReleaseKey = releaseKey,
    };

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    [Fact]
    public async Task Recalc_PurchaseMinusPurchaseReturn_UsingNetWeight()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 400m),
            Tx(sr.Key, StorageTransactionType.PurchaseReturn, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(250m, await ShippedAsync(sr.Key)); // 400 − 150
    }

    [Fact]
    public async Task Recalc_IgnoresCancelled_CountsPending()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 40m, StorageTransactionsStatus.Pending),
            Tx(sr.Key, StorageTransactionType.Purchase, 25m, StorageTransactionsStatus.Cancelled));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(40m, await ShippedAsync(sr.Key)); // pending conta, cancelled não
    }

    /// <summary>
    /// Liberação emitida por transferência de titularidade: a compra já foi registrada no
    /// confirm da transferência, então a Expedição não cria Purchase(8) nenhum. Quem
    /// consome a liberação é a perna de SAÍDA.
    /// </summary>
    [Fact]
    public async Task Recalc_OwnershipTransferRelease_CountsSalesShipmentMinusReturn()
    {
        var sr = await SeedReleaseAsync(1000m, ReleaseOrigin.OwnershipTransfer);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 400m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(250m, await ShippedAsync(sr.Key)); // 400 − 150
    }

    /// <summary>
    /// Simetria da regra: numa liberação de transferência o Purchase(8) não conta. Serve de
    /// rede contra dado legado do desenho antigo ser somado duas vezes.
    /// </summary>
    [Fact]
    public async Task Recalc_OwnershipTransferRelease_IgnoresPurchaseTypes()
    {
        var sr = await SeedReleaseAsync(1000m, ReleaseOrigin.OwnershipTransfer);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 500m),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 300m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(300m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresSalesTypes()
    {
        // tipos de venda pertencem ao fluxo de shipmentBilling, não ao de liberação
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 500m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 200m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresComplements()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.PurchaseQtyComplement, 50m),
            Tx(sr.Key, StorageTransactionType.PurchasePriceComplement, 70m),
            Tx(sr.Key, StorageTransactionType.Purchase, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresOtherReleases()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        var other = Guid.NewGuid();
        _db.Context.StorageTransactions.AddRange(
            Tx(other, StorageTransactionType.Purchase, 70m),
            Tx(sr.Key, StorageTransactionType.Purchase, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoTransactions_SetsZero()
    {
        var sr = await SeedReleaseAsync(released: 1000m);

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Theory]
    [InlineData(StorageTransactionType.Purchase, true)]
    [InlineData(StorageTransactionType.PurchaseReturn, true)]
    // Superconjunto: os hooks que consultam este predicado só têm a chave da liberação,
    // não a origem dela — quem decide o que soma é CalculateShippedAsync. Disparar um
    // recálculo a mais é idempotente; deixar de disparar perde o consumo da liberação.
    [InlineData(StorageTransactionType.SalesShipment, true)]
    [InlineData(StorageTransactionType.SalesShipmentReturn, true)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement, false)]
    [InlineData(StorageTransactionType.PurchasePriceComplement, false)]
    [InlineData(StorageTransactionType.Transfer, false)]
    public void AffectsShippedQuantity_MatchesRule(StorageTransactionType type, bool expected)
    {
        Assert.Equal(expected, ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(type));
    }

    /// <summary>
    /// Liberação emitida por uma DEVOLUÇÃO ao armazém: como na transferência, a Expedição não
    /// cria Purchase(8) — o grão já está no armazém, creditado pelo romaneio tipo 12. Quem
    /// consome a liberação é a perna de SAÍDA do reembarque.
    /// </summary>
    [Fact]
    public async Task Recalc_SalesReturnRelease_CountsSalesShipmentMinusReturn()
    {
        var sr = await SeedReleaseAsync(1000m, ReleaseOrigin.SalesReturn);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 400m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(250m, await ShippedAsync(sr.Key)); // 400 − 150
    }

    [Fact]
    public async Task Recalc_SalesReturnRelease_IgnoresPurchaseTypes()
    {
        var sr = await SeedReleaseAsync(1000m, ReleaseOrigin.SalesReturn);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 500m),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 300m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(300m, await ShippedAsync(sr.Key));
    }

    /// <summary>
    /// ⚠️ A trava aritmética que obriga o romaneio de devolução a nascer SEM
    /// <c>ShipmentReleaseKey</c>. Se ele carregasse a chave da liberação que gerou, cairia no
    /// SUBTRAENDO da fórmula acima: 30.000 devolvidos dariam <c>Shipped = −30.000</c> e
    /// <c>Available = 60.000</c> — a Expedição de Grãos ofereceria o DOBRO do grão que voltou.
    /// Este teste documenta o número; quem o garante são os dois serviços produtores.
    /// </summary>
    [Fact]
    public async Task Recalc_SalesReturnRelease_WouldGoNegative_IfTheReturnEntryCarriedTheKey()
    {
        var sr = await SeedReleaseAsync(30_000m, ReleaseOrigin.SalesReturn);
        _db.Context.StorageTransactions.Add(
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 30_000m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        var shipped = await ShippedAsync(sr.Key);
        Assert.Equal(-30_000m, shipped);

        var reloaded = await _db.Context.ShipmentReleases.FirstAsync(x => x.Key == sr.Key);
        Assert.Equal(60_000m, reloaded.AvailableQuantity); // o dobro — por isso a chave fica nula
    }

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedReturnScenarioAsync(ReleaseOrigin origin)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 10_000m,
            AllocatedVolume = 3_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        var purchase = Tx(Guid.Empty, StorageTransactionType.Purchase, 3_000m);
        purchase.ShipmentReleaseKey = null;
        var generator = Tx(Guid.Empty, StorageTransactionType.SalesShipmentReturn, 3_000m);
        generator.ShipmentReleaseKey = null;
        _db.Context.StorageTransactions.AddRange(purchase, generator);
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = purchase.Key,
            Volume = 3_000m,
            ApprovedAt = DateTime.Now,
            ApprovedBy = "tester",
        });
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 3_000m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            GeneratedByStorageTransactionKey = generator.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return (contract, release);
    }

    [Fact]
    public async Task Recalculate_SalesReturnRelease_SyncsTheContractLedger()
    {
        var (contract, release) = await SeedReturnScenarioAsync(ReleaseOrigin.SalesReturn);
        _db.Context.StorageTransactions.Add(Tx(release.Key, StorageTransactionType.SalesShipment, 1_000m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(release.Key);

        Assert.Equal(1_000m, await ShippedAsync(release.Key));
        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key);
        Assert.Equal(1_000m, reloaded.AllocatedVolume); // compra 3.000 − devolução 3.000 + saída 1.000
    }

    [Fact]
    public async Task Recalculate_StandardRelease_DoesNotTouchTheLedger()
    {
        var (contract, release) = await SeedReturnScenarioAsync(ReleaseOrigin.Standard);
        _db.Context.StorageTransactions.Add(Tx(release.Key, StorageTransactionType.Purchase, 500m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(release.Key);

        var allocations = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => a.PurchaseContractKey == contract.Key).ToListAsync();
        Assert.Single(allocations);
        Assert.Equal(3_000m, allocations[0].Volume);
    }
}
