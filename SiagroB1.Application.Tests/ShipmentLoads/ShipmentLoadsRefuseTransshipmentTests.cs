using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Recusa com destino Transbordo (GAC-1181, Task 8): a porta de entrada do cenário 3 do
/// chamado — carga faturada, recusada no destino, e a mercadoria segue para um armazém parceiro
/// para transbordo em vez de voltar direto a um armazém ou ficar disponível para refaturamento
/// no caminhão.
/// </summary>
/// <remarks>
/// Reaproveita a fixture de <see cref="ShipmentLoadsRefuseServiceTests"/>
/// (<c>BilledLoadAsync</c>/<c>SeedAsync</c>/<c>Service</c>/<c>BillingService</c>), que monta e
/// fatura a carga pelo caminho REAL — nenhuma fixture paralela nasce aqui.
/// </remarks>
public class ShipmentLoadsRefuseTransshipmentTests
{
    private const string TransshipmentWarehouse = ShipmentLoadsRefuseServiceTests.DestinationWarehouse;

    private readonly ShipmentLoadsRefuseServiceTests _fixture = new();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [ShipmentLoadsRefuseServiceTests.OriginWarehouse] = "ARMAZEM CEAGESP",
            [TransshipmentWarehouse] = "ARMAZEM RETAGUARDA",
        });

    private ShipmentLoadsTransshipmentRegisterEntryService RegisterEntryService() => new(
        _fixture._db,
        Warehouses(),
        new WarehouseComplementService(_fixture._db),
        _fixture.StorageCreate(Warehouses()),
        _fixture.StorageConfirm(),
        new ShipmentReleasesFromReturnService(_fixture._db.Context),
        new ShipmentLoadsMovementLogService(_fixture._db.Context));

    private ShipmentLoadsAttachTransactionsService AttachService() => new(
        _fixture._db, new ShipmentLoadsMovementLogService(_fixture._db.Context));

    private Task<ShipmentLoadTransshipment> TransshipmentAsync(Guid loadKey) =>
        _fixture._db.Context.ShipmentLoadsTransshipments
            .AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == loadKey);

    // ─── Destino: transbordo ───

    /// <summary>
    /// A recusa com destino Transbordo devolve o(s) documento(s) de saída — exatamente como
    /// Rebilling/Warehouse já fazem — e, além disso, abre a linha do transbordo.
    /// </summary>
    [Fact]
    public async Task Refuse_ToTransshipment_ReturnsTheInvoicesAndOpensTheTransshipment()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);

        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 30_000m, RefusalDestination.Transshipment, TransshipmentWarehouse),
            "tester");

        // A devolução aconteceu — mesma prova que os outros dois destinos já usam.
        var origin = await _fixture._db.Context.SalesInvoices
            .AsNoTracking().SingleAsync(x => x.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);

        var returnInvoice = await _fixture._db.Context.SalesInvoices
            .AsNoTracking().SingleAsync(x => x.InvoiceType == SalesInvoiceType.Return);
        Assert.Equal(InvoiceStatus.Confirmed, returnInvoice.InvoiceStatus);

        // E o transbordo abriu com os campos do briefing.
        var transshipment = await TransshipmentAsync(load.Key);
        Assert.Equal(TransshipmentOrigin.Refusal, transshipment.Origin);
        Assert.Equal(1, transshipment.Sequence);
        Assert.Equal(TransshipmentWarehouse, transshipment.WarehouseCode);
        Assert.Equal("ARMAZEM RETAGUARDA", transshipment.WarehouseName);
        Assert.Equal(30_000m, transshipment.OutgoingQuantity);
        Assert.Equal(decimal.Zero, transshipment.EntryQuantity);
        Assert.Null(transshipment.EntryStorageTransactionKey);
        Assert.Equal(DateTime.Today, transshipment.TransshipmentDate);

        // A narrativa do frete registra o transbordo com o delta e o saldo reais, não um
        // valor fixo que nasceria certo de qualquer jeito.
        var started = await _fixture._db.Context.ShipmentLoadMovements
            .AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key &&
                               x.MovementType == ShipmentLoadMovementType.TransshipmentStarted);
        Assert.Equal(-30_000m, started.Quantity);
        Assert.Equal(decimal.Zero, started.BalanceAfter);
        Assert.Equal(TransshipmentWarehouse, started.WarehouseCode);
        Assert.Equal("ARMAZEM RETAGUARDA", started.WarehouseName);
        Assert.Contains(TransshipmentWarehouse, started.Description);
        Assert.Contains("recusada", started.Description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ao contrário do destino Warehouse, a carga NÃO fecha em Returned — ela continua viva e o
    /// recálculo (via <c>HasOpenTransshipmentAsync</c>) a resolve como <c>InTransshipment</c>. Se
    /// este teste vir <c>Returned</c> é bug: a decisão 4 do briefing é explícita sobre isso.
    /// </summary>
    [Fact]
    public async Task Refuse_ToTransshipment_LeavesTheLoadInTransshipment()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);

        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 30_000m, RefusalDestination.Transshipment, TransshipmentWarehouse),
            "tester");

        var refused = await _fixture.LoadAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.InTransshipment, refused.Status);
        Assert.NotEqual(ShipmentLoadStatus.Returned, refused.Status);
        Assert.Equal(decimal.Zero, refused.InvoicedQuantity);
        Assert.Equal(decimal.Zero, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(30_000m, refused.TransshippedQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
    }

    /// <summary>
    /// O ramo Warehouse fica INTACTO: o destino Transbordo não cria o romaneio 12
    /// (<see cref="StorageTransactionType.SalesShipmentReturn"/>) nem a liberação
    /// <see cref="ReleaseOrigin.SalesReturn"/> — quem cria o romaneio de entrada é o
    /// <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>, depois, quando o caminhão for
    /// pesado no armazém parceiro.
    /// </summary>
    [Fact]
    public async Task Refuse_ToTransshipment_DoesNotCreateTheType12()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);

        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 30_000m, RefusalDestination.Transshipment, TransshipmentWarehouse),
            "tester");

        Assert.Empty(await _fixture._db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn)
            .ToListAsync());

        Assert.Empty(await _fixture._db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.SalesReturn)
            .ToListAsync());
    }

    /// <summary>
    /// Regressão do destino Warehouse: continua criando o romaneio 12 confirmado, emitindo a
    /// liberação <c>SalesReturn</c> e levando a carga a <c>Returned</c> — nada mudou para ele.
    /// </summary>
    [Fact]
    public async Task Refuse_ToWarehouse_StillBehavesLikeBefore()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);

        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 30_000m, RefusalDestination.Warehouse, TransshipmentWarehouse),
            "tester");

        var entry = await _fixture._db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(TransshipmentWarehouse, entry.WarehouseCode);
        Assert.Equal(30_000m, entry.GrossWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);

        var refused = await _fixture.LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.Returned, refused.Status);
        Assert.Equal(30_000m, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
        Assert.Equal(decimal.Zero, refused.TransshippedQuantity);

        Assert.Empty(await _fixture._db.Context.ShipmentLoadsTransshipments.AsNoTracking().ToListAsync());
    }

    /// <summary>Como no destino Warehouse, o armazém é obrigatório também para Transbordo.</summary>
    [Fact]
    public async Task Refuse_ToTransshipment_WithoutWarehouse_IsRefused()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => _fixture.Service().ExecuteAsync(
                ShipmentLoadsRefuseServiceTests.Request(
                    load, invoice, 30_000m, RefusalDestination.Transshipment, warehouseCode: null),
                "tester"));

        Assert.Contains("armazém de destino", error.Message);
        Assert.Empty(await _fixture._db.Context.ShipmentLoadsTransshipments.AsNoTracking().ToListAsync());
    }

    // ─── O cenário do cliente, ponta a ponta ───

    /// <summary>
    /// A tabela numérica do chamado: carga montada em SP com 30 t, faturada, recusada no porto
    /// com destino Transbordo, transbordada num armazém parceiro (perdendo 0,2 t na pesagem) e
    /// refaturada para OUTRO cliente — tudo na MESMA carga, como o CT-e único exige.
    /// </summary>
    [Fact]
    public async Task FullScenario_SpToPortRefusedThenPartnerThenSecondCustomer()
    {
        // 1) Carga de 30 t, faturada por inteiro (Expedição de origem).
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);
        var billed = await _fixture.LoadAsync(load.Key);
        Assert.Equal(30_000m, billed.TotalQuantity);
        Assert.Equal(30_000m, billed.InvoicedQuantity);
        Assert.Equal(decimal.Zero, billed.AvailableQuantity);

        // 2) Recusada no porto, destino Transbordo.
        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, 30_000m, RefusalDestination.Transshipment, TransshipmentWarehouse),
            "tester");

        var afterRefusal = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, afterRefusal.AvailableQuantity);
        Assert.Equal(30_000m, afterRefusal.TransshippedQuantity);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, afterRefusal.Status);

        var transshipment = await TransshipmentAsync(load.Key);
        Assert.Equal(30_000m, transshipment.OutgoingQuantity);

        // 3) Entrada no armazém parceiro: 29,8 t pesadas (0,2 t de quebra de transporte).
        var registered = await RegisterEntryService().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, DateTime.Today, null, "tester");
        Assert.Equal(29_800m, registered.EntryQuantity);
        Assert.Equal(200m, registered.ShrinkageQuantity);

        // 4) Vincula a saída do transbordo (recarga): 29,5 t.
        var exit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R2",
            CardCode = ShipmentLoadsRefuseServiceTests.CardCode,
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            TruckCode = load.TruckCode,
            GrossWeight = 29_500m,
            NetWeight = 29_500m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _fixture._db.Context.StorageTransactions.Add(exit);
        await _fixture._db.SaveChangesAsync();

        await AttachService().ExecuteAsync(load.Key, [exit.Key], transshipment.Key, "tester");

        var afterAttach = await _fixture.LoadAsync(load.Key);
        Assert.Equal(59_500m, afterAttach.TotalQuantity);
        Assert.Equal(29_500m, afterAttach.AvailableQuantity);

        // 5) Fatura para OUTRO cliente (outro local de entrega — mesma convenção já usada pela
        // suíte de recusa para representar um destino comercial diferente).
        var secondInvoice = ShipmentLoadsRefuseServiceTests.InvoiceFor(
            load, (await _fixture._db.Context.SalesContracts.AsNoTracking().SingleAsync()),
            (await _fixture._db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync()),
            29_500m,
            deliveryCardCode: "D0002");

        await _fixture.BillingService().ExecuteAsync(secondInvoice, "tester");

        var final = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, final.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, final.Status);
        Assert.Equal(29_500m, final.InvoicedQuantity);
        Assert.Equal(30_000m, final.TransshippedQuantity);
    }
}
