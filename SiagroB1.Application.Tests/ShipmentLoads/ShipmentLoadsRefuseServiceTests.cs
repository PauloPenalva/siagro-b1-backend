using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Recusa/devolução de carga faturada, nos dois destinos físicos.
/// </summary>
/// <remarks>
/// A carga é montada e faturada pelo caminho REAL
/// (<see cref="ShipmentBillingCreateSalesInvoiceService"/>) antes de cada recusa: testar a
/// recusa sobre um estado inventado à mão provaria menos do que parece, porque metade das
/// armadilhas está justamente em como o faturamento por carga deixa as coisas (nota sem
/// romaneio, romaneio sem nota).
/// </remarks>
public class ShipmentLoadsRefuseServiceTests
{
    private const string CardCode = "C0001";
    private const string OriginWarehouse = "ARM01";
    private const string DestinationWarehouse = "ARM99";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IBusinessPartnerService Partners() =>
        new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });

    private static FakeItemService Items() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM CEAGESP",
            [DestinationWarehouse] = "ARMAZEM RETAGUARDA",
        });

    private SalesInvoicesCreateService CreateService()
    {
        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var partners = Partners();

        return new SalesInvoicesCreateService(
            _db,
            partners,
            Items(),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(_db, usages, partners),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private SalesInvoicesConfirmService ConfirmService() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesInvoicesUsageGuardService(
                new UsageService(_db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBalanceHookService(_db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private StorageTransactionsCreateService StorageCreate(IWarehouseService? warehouses = null) =>
        new(_db,
            new FakeDocNumberSequenceService(),
            Partners(),
            Items(),
            warehouses ?? Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private ShipmentLoadsRefuseService Service(IWarehouseService? warehouses = null) =>
        new(_db,
            CreateService(),
            ConfirmService(),
            StorageCreate(warehouses),
            StorageConfirm(),
            new ShipmentLoadsMovementLogService(_db.Context),
            warehouses ?? Warehouses(),
            NullLogger<ShipmentLoadsRefuseService>.Instance);

    private ShipmentBillingCreateSalesInvoiceService BillingService()
    {
        return new ShipmentBillingCreateSalesInvoiceService(
            _db,
            CreateService(),
            new ShipmentBillingTransactionGuardService(_db.Context),
            new SalesShipmentReleaseMovementGuardService(_db.Context),
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBillingGuardService(_db.Context),
            new ShipmentLoadsRecalculateInvoicedService(_db),
            new ShipmentLoadsMovementLogService(_db.Context),
            NullLogger<ShipmentBillingCreateSalesInvoiceService>.Instance);
    }

    private async Task<(ShipmentLoad Load, SalesContract Contract, SalesShipmentRelease Release,
        StorageTransaction Shipment)> SeedAsync()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            TotalQuantity = 40_000m,
            Status = ShipmentLoadStatus.Open,
        };

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        var release = SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 1_000_000m);

        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(shipment);
        _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });
        await _db.SaveChangesAsync();

        return (load, contract, release, shipment);
    }

    private static SalesInvoice InvoiceFor(
        ShipmentLoad load, SalesContract contract, SalesShipmentRelease release, decimal quantity,
        string deliveryCardCode = "D0001")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            BranchCode = "01",
            ShipmentLoadKey = load.Key,
            DeliveryCardCode = deliveryCardCode,
            InvoiceStatus = InvoiceStatus.Pending,
            InvoiceType = SalesInvoiceType.Normal,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            UnitPrice = 90m,
            SalesContractKey = contract.Key,
            SalesShipmentReleaseKey = release.Key,
        });

        return invoice;
    }

    /// <summary>Monta e fatura a carga inteira, devolvendo a nota emitida.</summary>
    private async Task<(ShipmentLoad Load, SalesInvoice Invoice)> BilledLoadAsync(decimal quantity = 40_000m)
    {
        var (load, contract, release, _) = await SeedAsync();
        var invoice = InvoiceFor(load, contract, release, quantity);

        await BillingService().ExecuteAsync(invoice, "tester");

        return (load, invoice);
    }

    private static RefusalRequest Request(
        ShipmentLoad load,
        SalesInvoice invoice,
        decimal quantity,
        RefusalDestination destination = RefusalDestination.Rebilling,
        string? warehouseCode = null) =>
        new(load.Key,
            [new RefusalLine(invoice.Key, quantity)],
            destination,
            warehouseCode,
            "Recusado por qualidade no porto");

    private Task<ShipmentLoad> LoadAsync(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    // ─── Destino: o caminhão segue para outro destino (refaturamento) ───

    /// <summary>
    /// Recusa TOTAL para refaturamento: o saldo inteiro volta e a carga reaparece disponível.
    /// É o caminho que faz "disponibilizar a carga para faturamento" acontecer.
    /// </summary>
    [Fact]
    public async Task A_total_refusal_for_rebilling_returns_the_whole_balance()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 40_000m), "tester");

        var refused = await LoadAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Open, refused.Status);
        Assert.Equal(decimal.Zero, refused.InvoicedQuantity);
        Assert.Equal(decimal.Zero, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(40_000m, refused.AvailableQuantity);
    }

    /// <summary>
    /// Recusa PARCIAL: só o volume recusado volta, e o resto continua faturado para o cliente
    /// original.
    /// </summary>
    [Fact]
    public async Task A_partial_refusal_returns_only_the_refused_volume()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 15_000m), "tester");

        var refused = await LoadAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, refused.Status);
        Assert.Equal(25_000m, refused.InvoicedQuantity);
        Assert.Equal(15_000m, refused.AvailableQuantity);
    }

    /// <summary>
    /// A origem de uma recusa PARCIAL continua Confirmada e com a entrega aberta — é o que
    /// permite a SEGUNDA recusa do mesmo documento. Carimbá-la como Returned a fecharia e a
    /// segunda tentativa morreria em "Invoice closed.", sem saída pela tela.
    /// </summary>
    [Fact]
    public async Task A_partial_refusal_leaves_the_origin_confirmed_and_open()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 15_000m), "tester");

        var origin = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);

        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, origin.DeliveryStatus);
    }

    /// <summary>Recusa total marca a origem como Retornada, como o caminho de sempre.</summary>
    [Fact]
    public async Task A_total_refusal_marks_the_origin_as_returned()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 40_000m), "tester");

        var origin = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);

        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
    }

    /// <summary>
    /// Duas recusas parciais do mesmo documento acumulam, e a terceira que ultrapassa o total é
    /// recusada com o saldo devolvível na mensagem.
    /// </summary>
    [Fact]
    public async Task Sequential_partial_refusals_accumulate_and_the_excess_is_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 15_000m), "tester");
        await Service().ExecuteAsync(Request(load, invoice, 10_000m), "tester");

        var refused = await LoadAsync(load.Key);
        Assert.Equal(15_000m, refused.InvoicedQuantity);

        var excess = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(load, invoice, 20_000m), "tester"));

        Assert.Contains("maior que o saldo devolvível", excess.Message);
    }

    /// <summary>A devolução criada aponta a MESMA carga da origem — é o que liga as duas na fórmula.</summary>
    [Fact]
    public async Task The_return_document_points_at_the_same_load()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(Request(load, invoice, 40_000m), "tester");

        var returnInvoice = await _db.Context.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.InvoiceType == SalesInvoiceType.Return);

        Assert.Equal(load.Key, returnInvoice.ShipmentLoadKey);
        Assert.Equal(invoice.Key, returnInvoice.SalesInvoiceOriginKey);
        Assert.Equal(InvoiceStatus.Confirmed, returnInvoice.InvoiceStatus);
    }

    // ─── Destino: a mercadoria volta para um armazém ───

    /// <summary>
    /// O caso do cliente: carregou no CEAGESP, recusaram no porto, a mercadoria voltou para um
    /// armazém DIFERENTE do de origem. Um único romaneio de devolução, no armazém escolhido.
    /// </summary>
    [Fact]
    public async Task A_refusal_to_a_warehouse_creates_one_return_shipment_at_the_chosen_warehouse()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Equal(DestinationWarehouse, entry.WarehouseCode);
        Assert.NotEqual(load.WarehouseCode, entry.WarehouseCode);
        Assert.Equal(40_000m, entry.GrossWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(load.Key, entry.RefusedFromShipmentLoadKey);
    }

    /// <summary>
    /// As TRÊS chaves que o romaneio de devolução não pode carregar, cada uma com um estrago
    /// próprio: <c>ShipmentLoadKey</c> infla o volume embarcado da carga,
    /// <c>ShipmentReleaseKey</c> move saldo de liberação de compra e <c>ReturnInvoiceKey</c> faz
    /// o estorno de confirmação sequestrar este romaneio.
    /// </summary>
    [Fact]
    public async Task The_return_shipment_carries_none_of_the_three_forbidden_keys()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ReturnInvoiceKey);
    }

    /// <summary>
    /// A simetria do anterior: o volume EMBARCADO da carga não pode crescer por causa da
    /// devolução.
    /// </summary>
    [Fact]
    public async Task A_refusal_to_a_warehouse_does_not_inflate_the_loads_total_quantity()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        await ShipmentLoadsRecalculateTotalService.RecalculateAsync(_db.Context, load.Key);
        await _db.SaveChangesAsync();

        Assert.Equal(40_000m, (await LoadAsync(load.Key)).TotalQuantity);
    }

    /// <summary>
    /// Devolvida ao armazém, a carga fica SEM saldo e encerrada — senão voltaria a se oferecer
    /// no Faturamento de Expedição com a mercadoria já creditada em outro armazém.
    /// </summary>
    [Fact]
    public async Task A_total_refusal_to_a_warehouse_leaves_the_load_closed_without_balance()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var refused = await LoadAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Returned, refused.Status);
        Assert.Equal(40_000m, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
    }

    /// <summary>
    /// Recusa PARCIAL ao armazém: 15 voltam ao armazém, 25 seguem faturados. O saldo da carga
    /// fica zero — não há nada a refaturar, porque o que voltou saiu da carga.
    /// </summary>
    [Fact]
    public async Task A_partial_refusal_to_a_warehouse_removes_only_the_returned_volume()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 15_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var refused = await LoadAsync(load.Key);

        Assert.Equal(25_000m, refused.InvoicedQuantity);
        Assert.Equal(15_000m, refused.ReturnedToWarehouseQuantity);
        Assert.Equal(decimal.Zero, refused.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, refused.Status);
    }

    /// <summary>
    /// O ponto do "retaguarda": a mercadoria devolvida tem de ficar DISPONÍVEL para novo
    /// embarque no armazém de destino. Prova pelo caminho real — confirmar um novo romaneio de
    /// embarque naquele armazém, que só passa se o saldo estiver lá.
    /// </summary>
    [Fact]
    public async Task The_returned_goods_become_available_for_a_new_shipment_at_the_destination()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var newShipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R2",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            GrossWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Pending,
        };

        _db.Context.StorageTransactions.Add(newShipment);
        await _db.SaveChangesAsync();

        // Não estoura: o saldo do armazém de destino cobre o embarque.
        await StorageConfirm().ExecuteAsync(newShipment, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, newShipment.TransactionStatus);
    }

    /// <summary>
    /// O contraponto que dá sentido ao teste anterior: sem a devolução, o mesmo embarque no
    /// mesmo armazém é RECUSADO por falta de saldo. Sem esta prova, aquele passaria mesmo que a
    /// devolução não creditasse nada.
    /// </summary>
    [Fact]
    public async Task Without_the_refusal_the_destination_warehouse_has_no_balance_to_ship()
    {
        await BilledLoadAsync();

        var newShipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R2",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            GrossWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Pending,
        };

        _db.Context.StorageTransactions.Add(newShipment);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => StorageConfirm().ExecuteAsync(newShipment, "tester"));
    }

    // ─── Narrativa do frete ───

    /// <summary>
    /// A movimentação precisa contar a viagem: para onde foi, por que voltou e para qual
    /// armazém. É o que o financeiro lê para pagar o frete.
    /// </summary>
    [Fact]
    public async Task The_movement_history_records_customer_delivery_location_reason_and_warehouse()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var movements = await _db.Context.ShipmentLoadMovements
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == load.Key)
            .ToListAsync();

        var billed = movements.Single(x => x.MovementType == ShipmentLoadMovementType.Billed);
        Assert.Equal(CardCode, billed.CardCode);
        Assert.Equal("D0001", billed.DeliveryCardCode);

        var refusal = movements.Single(x => x.MovementType == ShipmentLoadMovementType.Refused);
        Assert.Equal("Recusado por qualidade no porto", refusal.Reason);
        Assert.Equal(CardCode, refusal.CardCode);
        Assert.Equal("D0001", refusal.DeliveryCardCode);

        var toWarehouse = movements.Single(x => x.MovementType == ShipmentLoadMovementType.ReturnedToWarehouse);
        Assert.Equal(DestinationWarehouse, toWarehouse.WarehouseCode);
        Assert.Equal("ARMAZEM RETAGUARDA", toWarehouse.WarehouseName);
        Assert.Equal(-40_000m, toWarehouse.Quantity);
        Assert.Equal(decimal.Zero, toWarehouse.BalanceAfter);
        Assert.NotNull(toWarehouse.StorageTransactionKey);
    }

    /// <summary>
    /// Refaturar depois da recusa grava o NOVO cliente e o NOVO local de entrega — é o par de
    /// linhas que mostra os dois destinos da mesma carga.
    /// </summary>
    [Fact]
    public async Task Rebilling_after_a_refusal_records_the_new_destination_in_the_history()
    {
        var (load, contract, release, _) = await SeedAsync();
        var first = InvoiceFor(load, contract, release, 40_000m, deliveryCardCode: "D0001");
        await BillingService().ExecuteAsync(first, "tester");

        await Service().ExecuteAsync(Request(load, first, 40_000m), "tester");

        var second = InvoiceFor(load, contract, release, 40_000m, deliveryCardCode: "D0002");
        await BillingService().ExecuteAsync(second, "tester");

        var billings = await _db.Context.ShipmentLoadMovements
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == load.Key &&
                        x.MovementType == ShipmentLoadMovementType.Billed)
            .OrderBy(x => x.RowId)
            .ToListAsync();

        Assert.Equal(2, billings.Count);
        Assert.Equal("D0001", billings[0].DeliveryCardCode);
        Assert.Equal("D0002", billings[1].DeliveryCardCode);
    }

    // ─── Guardas e atomicidade ───

    [Fact]
    public async Task A_refusal_without_a_reason_is_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        var request = new RefusalRequest(
            load.Key, [new RefusalLine(invoice.Key, 40_000m)],
            RefusalDestination.Rebilling, null, "   ");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(request, "tester"));

        Assert.Contains("motivo da recusa", error.Message);
    }

    [Fact]
    public async Task A_warehouse_refusal_without_a_warehouse_is_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(load, invoice, 40_000m, RefusalDestination.Warehouse, warehouseCode: null),
                "tester"));

        Assert.Contains("armazém de destino", error.Message);
    }

    [Fact]
    public async Task An_unknown_destination_warehouse_is_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(load, invoice, 40_000m, RefusalDestination.Warehouse, "NAOEXISTE"),
                "tester"));

        Assert.Contains("não encontrado", error.Message);
    }

    [Fact]
    public async Task A_document_of_another_load_is_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        var otherLoad = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000008",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "XYZ9W87",
            TotalQuantity = 10_000m,
            InvoicedQuantity = 10_000m,
            Status = ShipmentLoadStatus.Invoiced,
        };
        _db.Context.ShipmentLoads.Add(otherLoad);
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(otherLoad, invoice, 40_000m), "tester"));

        Assert.Contains("não pertence à carga", error.Message);
    }

    [Fact]
    public async Task Zero_quantities_are_refused_before_anything_is_written()
    {
        var (load, invoice) = await BilledLoadAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(load, invoice, decimal.Zero), "tester"));

        Assert.Empty(await _db.Context.SalesInvoices
            .AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .ToListAsync());

        Assert.Equal(40_000m, (await LoadAsync(load.Key)).InvoicedQuantity);
    }

    /// <summary>
    /// <b>O teste que prova a transação única.</b> A entrada no armazém falha DEPOIS de as
    /// devoluções já terem sido criadas e confirmadas; nada pode sobrar gravado.
    /// </summary>
    /// <remarks>
    /// Se algum serviço interno for chamado em <c>CommitMode.Auto</c>, ele comita a transação
    /// deste serviço no meio do caminho e a devolução sobrevive à falha — deixando a carga
    /// reaberta para faturamento com a mercadoria fisicamente fora. É o pior modo de falha da
    /// feature, e este é o único teste que o pega.
    /// <para>
    /// ⚠️ O provider InMemory não tem transação de verdade (<c>TransactionIgnoredWarning</c> é
    /// suprimido em <c>TestDb</c>), então o rollback aqui NÃO é exercido pelo banco. O que este
    /// teste garante é que a falha acontece antes de qualquer efeito no saldo da carga e que o
    /// serviço propaga o erro em vez de engoli-lo — a atomicidade real depende da validação
    /// prévia do armazém, que é o que o torna determinístico.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_failure_resolving_the_warehouse_leaves_no_return_behind()
    {
        var (load, invoice) = await BilledLoadAsync();

        // Armazém inexistente: a resolução falha ANTES de abrir a transação, que é onde a
        // validação toda deve acontecer.
        await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(load, invoice, 40_000m, RefusalDestination.Warehouse, "INEXISTENTE"),
                "tester"));

        Assert.Empty(await _db.Context.SalesInvoices
            .AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .ToListAsync());

        Assert.Empty(await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn)
            .ToListAsync());

        var untouched = await LoadAsync(load.Key);
        Assert.Equal(40_000m, untouched.InvoicedQuantity);
        Assert.Equal(decimal.Zero, untouched.ReturnedToWarehouseQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, untouched.Status);
    }

    /// <summary>Carga cancelada é estado terminal — não há o que recusar.</summary>
    [Fact]
    public async Task A_cancelled_load_cannot_be_refused()
    {
        var (load, invoice) = await BilledLoadAsync();

        var tracked = await _db.Context.ShipmentLoads.SingleAsync(x => x.Key == load.Key);
        tracked.Status = ShipmentLoadStatus.Cancelled;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(load, invoice, 40_000m), "tester"));

        Assert.Contains("cancelada", error.Message);
    }

    /// <summary>
    /// Depois de recusada ao armazém, a carga não pode voltar a ser faturada — o guard decide
    /// pelo status, com a mensagem que explica o porquê.
    /// </summary>
    [Fact]
    public async Task A_load_returned_to_a_warehouse_cannot_be_billed_again()
    {
        var (load, contract, release, _) = await SeedAsync();
        var invoice = InvoiceFor(load, contract, release, 40_000m);
        await BillingService().ExecuteAsync(invoice, "tester");

        await Service().ExecuteAsync(
            Request(load, invoice, 40_000m, RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => BillingService().ExecuteAsync(
                InvoiceFor(load, contract, release, 10_000m), "tester"));

        Assert.Contains("devolvida ao armazém", error.Message);
    }
}
