using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Tela de conferência de entregas (/sales-invoices/reconciliation): ao encerrar o item
/// (Closed), o contrato deve passar a consumir o líquido (DeliveredQuantity − QuantityLoss)
/// em vez do nominal. O UI5 salva por PATCH, e no PATCH o controller carrega a entidade
/// RASTREADA e aplica o Delta nela antes de chamar o serviço — por isso os testes do
/// caminho PATCH passam a MESMA instância como "entity".
/// </summary>
public class SalesInvoicesItemsUpdateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesItemsUpdateService Service() => new(
        _db,
        new FakeItemService(new Dictionary<string, string>
        {
            ["SOJA"] = "SOJA EM GRAOS",
            ["MILHO"] = "MILHO EM GRAOS",
        }),
        new TestLogger<SalesInvoicesUpdateService>());

    private static SalesContract NewContract(decimal totalVolume, decimal allocatedVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = Guid.NewGuid().ToString("N")[..8],
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Status = ContractStatus.Approved,
    };

    private static SalesInvoiceItem NewItem(
        decimal quantity,
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal delivered = 0m, decimal loss = 0m) => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        Quantity = quantity,
        DeliveredQuantity = delivered,
        QuantityLoss = loss,
        DeliveryStatus = deliveryStatus,
    };

    /// <summary>Monta contrato + item + alocação nominal e devolve as duas chaves.</summary>
    private async Task<(SalesContract Contract, SalesInvoiceItem Item)> SeedAsync(
        decimal quantity = 100m,
        decimal allocatedVolume = 100m,
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal delivered = 0m, decimal loss = 0m)
    {
        var contract = NewContract(totalVolume: 1000m, allocatedVolume: allocatedVolume);
        var item = NewItem(quantity, deliveryStatus, delivered, loss);

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            SalesInvoiceItemKey = item.Key!.Value,
            Volume = quantity,
            Origin = SalesContractAllocationOrigin.Billing,
        });
        await _db.Context.SaveChangesAsync();

        return (contract, item);
    }

    /// <summary>
    /// Pendura uma liberação de entrega na alocação já semeada, com o saldo consumido
    /// coerente com o estado nominal (o que o faturamento teria gravado).
    /// </summary>
    private async Task<SalesShipmentRelease> SeedReleaseForAsync(
        SalesContract contract, decimal shipped,
        ReleaseStatus status = ReleaseStatus.Actived)
    {
        var release = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            DeliveryLocationCode = "C0001",
            ReleasedQuantity = 500m,
            ShippedQuantity = shipped,
            Status = status,
        };
        _db.Context.SalesShipmentReleases.Add(release);

        var allocation = await _db.Context.SalesContractsAllocations
            .SingleAsync(a => a.SalesContractKey == contract.Key);
        allocation.SalesShipmentReleaseKey = release.Key;

        await _db.Context.SaveChangesAsync();
        return release;
    }

    private async Task<decimal> AllocatedAsync(Guid key) =>
        (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == key)).AllocatedVolume;

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    private async Task<SalesInvoiceItem> StoredItemAsync(Guid key) =>
        await _db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == key);

    /// <summary>
    /// Reproduz o GAC-1129: no PATCH a entidade chega já rastreada e mutada, então
    /// comparar "existente" com "novo" compara o objeto com ele mesmo. O recálculo tem
    /// de disparar mesmo assim.
    /// </summary>
    [Fact]
    public async Task PatchPath_ClosingWithLoss_RecalculatesContractBalance()
    {
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 100m);

        // Caminho do controller: carrega rastreado e muta a MESMA instância.
        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = 95m;
        tracked.QuantityLoss = 5m;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(90m, await AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task PutPath_ClosingWithLoss_RecalculatesContractBalance()
    {
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 100m);

        // Caminho PUT: a entidade vem do body, desanexada.
        var incoming = NewItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 95m, loss: 5m);
        incoming.Key = item.Key;

        await Service().ExecuteAsync(item.Key!.Value, incoming, "tester");

        Assert.Equal(90m, await AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task ReopeningClosedItem_RestoresNominalBalance()
    {
        var (contract, item) = await SeedAsync(
            quantity: 100m, allocatedVolume: 90m,
            deliveryStatus: SalesInvoiceDeliveryStatus.Closed, delivered: 95m, loss: 5m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(100m, await AllocatedAsync(contract.Key));
    }

    /// <summary>
    /// A liberação segue a MESMA regra do contrato: encerrar a entrega passa a consumir o
    /// líquido, devolvendo a quebra ao saldo liberado.
    /// </summary>
    [Fact]
    public async Task ClosingWithLoss_RecalculatesShipmentReleaseBalance()
    {
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 100m);
        var release = await SeedReleaseForAsync(contract, shipped: 100m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = 95m;
        tracked.QuantityLoss = 5m;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(90m, await ShippedAsync(release.Key));
    }

    [Fact]
    public async Task ReopeningClosedItem_RestoresShipmentReleaseNominalBalance()
    {
        var (contract, item) = await SeedAsync(
            quantity: 100m, allocatedVolume: 90m,
            deliveryStatus: SalesInvoiceDeliveryStatus.Closed, delivered: 95m, loss: 5m);
        var release = await SeedReleaseForAsync(contract, shipped: 90m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(100m, await ShippedAsync(release.Key));
    }

    /// <summary>
    /// Liberação finalizada/cancelada fica congelada, como o contrato encerrado — quem
    /// precisa mexer usa o recálculo manual.
    /// </summary>
    [Theory]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task ClosingWithLoss_FrozenRelease_KeepsShippedQuantity(ReleaseStatus status)
    {
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 100m);
        var release = await SeedReleaseForAsync(contract, shipped: 100m, status: status);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = 95m;
        tracked.QuantityLoss = 5m;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(100m, await ShippedAsync(release.Key));
    }

    [Fact]
    public async Task EditWithoutDeliveryChange_DoesNotRecalculate()
    {
        // AllocatedVolume propositalmente divergente: se o recálculo disparasse à toa,
        // viraria 100 e o teste acusaria.
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 999m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.ItemCode = "MILHO";

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal(999m, await AllocatedAsync(contract.Key));
        Assert.Equal("MILHO EM GRAOS", (await StoredItemAsync(item.Key!.Value)).ItemName);
    }

    [Theory]
    [InlineData(0, 0)]     // nada digitado
    [InlineData(50, 50)]   // desconto zera o líquido
    [InlineData(50, 60)]   // desconto maior que o entregue
    public async Task ClosingWithNonPositiveNet_Throws_AndKeepsItemOpen(int delivered, int loss)
    {
        var (contract, item) = await SeedAsync(quantity: 100m, allocatedVolume: 100m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = delivered;
        tracked.QuantityLoss = loss;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteAsync(item.Key!.Value, tracked, "tester"));

        Assert.Equal(SalesInvoiceDeliveryStatus.Open,
            (await StoredItemAsync(item.Key!.Value)).DeliveryStatus);
        Assert.Equal(100m, await AllocatedAsync(contract.Key));
    }

    /// <summary>
    /// Registro legado já gravado como Closed com líquido zerado não pode ficar travado
    /// para edições que não mexem na entrega.
    /// </summary>
    [Fact]
    public async Task AlreadyClosedWithZeroNet_EditingOtherField_DoesNotThrow()
    {
        var (_, item) = await SeedAsync(
            quantity: 100m, allocatedVolume: 0m,
            deliveryStatus: SalesInvoiceDeliveryStatus.Closed, delivered: 0m, loss: 0m);

        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.ItemCode = "MILHO";

        await Service().ExecuteAsync(item.Key!.Value, tracked, "tester");

        Assert.Equal("MILHO EM GRAOS", (await StoredItemAsync(item.Key!.Value)).ItemName);
    }
}
