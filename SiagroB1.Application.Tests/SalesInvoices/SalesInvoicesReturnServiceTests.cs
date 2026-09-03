using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Retorno de um documento de saída LEGADO (romaneios ligados direto à nota), nos dois destinos
/// físicos e em quantidade parcial ou total.
/// </summary>
/// <remarks>
/// Aqui o parcial se expressa ESCOLHENDO ROMANEIOS, e não digitando quantidade: cada romaneio é
/// uma carreta, e meia carreta não volta do pátio.
/// </remarks>
public class SalesInvoicesReturnServiceTests
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
            new ShipmentLoadsBalanceHookService(
                _db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            Partners(),
            Items(),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private SalesInvoicesReturnService Service() =>
        new(_db,
            CreateService(),
            ConfirmService(),
            StorageCreate(),
            StorageConfirm(),
            Warehouses(),
            NullLogger<SalesInvoicesReturnService>.Instance);

    /// <summary>
    /// Nota legada confirmada, com dois romaneios faturados de 20.000 cada — o formato que a
    /// rotina de faturamento antiga produz: romaneios em <c>SalesTransactions</c> e sem carga.
    /// </summary>
    private async Task<(SalesInvoice Invoice, StorageTransaction R1, StorageTransaction R2)> SeedAsync(
        Guid? shipmentLoadKey = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            CardName = "CLIENTE TESTE",
            BranchCode = "01",
            InvoiceNumber = "000001",
            TaxDocumentNumber = "12345",
            TaxDocumentSeries = "1",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = shipmentLoadKey,
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            Quantity = 40_000m,
            UnitPrice = 90m,
        });

        var r1 = Shipment(invoice, "R1");
        var r2 = Shipment(invoice, "R2");

        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.StorageTransactions.AddRange(r1, r2);

        // A filial é compartilhada: o teste do romaneio alheio semeia DUAS notas, e adicioná-la
        // de novo derrubaria o seed antes de o serviço ser exercido.
        if (!await _db.Context.Branchs.AnyAsync(x => x.Code == "01"))
            _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });

        await _db.SaveChangesAsync();

        return (invoice, r1, r2);
    }

    private static StorageTransaction Shipment(SalesInvoice invoice, string code) =>
        new()
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = CardCode,
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
            InvoiceQty = 20_000m,
            IsInvoiced = true,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced,
            SalesInvoiceKey = invoice.Key,
        };

    /// <summary>
    /// Monta o pedido de retorno. <paramref name="quantities"/> ausente significa devolver cada
    /// romaneio INTEIRO — o caso comum, e o único que o destino "segue viagem" aceita.
    /// </summary>
    private static SalesInvoiceReturnRequest Request(
        SalesInvoice invoice,
        IReadOnlyList<Guid> shipmentKeys,
        RefusalDestination destination = RefusalDestination.Rebilling,
        string? warehouseCode = null,
        string reason = "Recusado por qualidade no destino",
        IReadOnlyList<decimal?>? quantities = null) =>
        new(invoice.Key,
            [.. shipmentKeys.Select((key, index) =>
                new SalesInvoiceReturnShipment(key, quantities?[index]))],
            destination,
            warehouseCode,
            reason);

    private Task<StorageTransaction> ShipmentAsync(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<SalesInvoice> InvoiceAsync(Guid key) =>
        _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == key);

    // ─── Destino: o caminhão segue para outro destino (refaturamento) ───

    /// <summary>
    /// Retorno PARCIAL para refaturamento: só o romaneio escolhido volta a ficar disponível.
    /// É o que faz a mercadoria reaparecer no faturamento e na Montagem de Carga.
    /// </summary>
    [Fact]
    public async Task A_partial_rebilling_return_frees_only_the_chosen_shipment()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester");

        var freed = await ShipmentAsync(r1.Key);
        var untouched = await ShipmentAsync(r2.Key);

        Assert.Equal(StorageTransactionsStatus.Confirmed, freed.TransactionStatus);
        Assert.Null(freed.SalesInvoiceKey);

        Assert.Equal(StorageTransactionsStatus.Invoiced, untouched.TransactionStatus);
        Assert.Equal(invoice.Key, untouched.SalesInvoiceKey);
    }

    /// <summary>
    /// A origem de um retorno PARCIAL continua Confirmada e com a entrega aberta — é o que permite
    /// o SEGUNDO retorno da mesma nota. Fechá-la faria a segunda tentativa morrer em
    /// "documento já encerrado", sem saída pela tela.
    /// </summary>
    [Fact]
    public async Task A_partial_return_leaves_the_origin_confirmed_and_open()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester");

        var origin = await InvoiceAsync(invoice.Key);

        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, origin.DeliveryStatus);
    }

    /// <summary>Retorno TOTAL marca a origem como Retornada e fecha a entrega, como sempre.</summary>
    [Fact]
    public async Task A_total_return_marks_the_origin_as_returned_and_closed()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(Request(invoice, [r1.Key, r2.Key]), "tester");

        var origin = await InvoiceAsync(invoice.Key);

        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, origin.DeliveryStatus);
    }

    /// <summary>
    /// Dois retornos parciais da mesma nota acumulam: o segundo fecha o que o primeiro deixou
    /// aberto.
    /// </summary>
    [Fact]
    public async Task Sequential_partial_returns_accumulate_until_the_origin_closes()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester");
        Assert.Equal(InvoiceStatus.Confirmed, (await InvoiceAsync(invoice.Key)).InvoiceStatus);

        await Service().ExecuteAsync(Request(invoice, [r2.Key]), "tester");
        Assert.Equal(InvoiceStatus.Returned, (await InvoiceAsync(invoice.Key)).InvoiceStatus);
    }

    /// <summary>
    /// A devolução nasce CONFIRMADA: é a confirmação que devolve o saldo e move os romaneios, e
    /// deixá-la pendente significaria a mercadoria não voltar a lugar nenhum.
    /// </summary>
    [Fact]
    public async Task The_return_document_is_created_already_confirmed()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester");

        var saved = await InvoiceAsync(returnInvoice.Key);

        Assert.Equal(SalesInvoiceType.Return, saved.InvoiceType);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(invoice.Key, saved.SalesInvoiceOriginKey);
    }

    /// <summary>
    /// A devolução parcial leva a quantidade dos romaneios escolhidos, e não a nota inteira.
    /// </summary>
    [Fact]
    public async Task A_partial_return_carries_the_quantity_of_the_chosen_shipments()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester");

        var items = await _db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == returnInvoice.Key)
            .ToListAsync();

        Assert.Equal(20_000m, Assert.Single(items).Quantity);
    }

    /// <summary>O motivo fica no Comments dos dois documentos — é onde o operador vai procurar.</summary>
    [Fact]
    public async Task The_reason_is_recorded_on_both_documents()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(
            Request(invoice, [r1.Key], reason: "Carreta recusada na balança do porto"), "tester");

        Assert.Contains("Carreta recusada na balança do porto",
            (await InvoiceAsync(returnInvoice.Key)).Comments);

        Assert.Contains("Carreta recusada na balança do porto",
            (await InvoiceAsync(invoice.Key)).Comments);
    }

    // ─── Destino: a mercadoria volta para um armazém ───

    /// <summary>
    /// Devolução ao armazém: nasce UM romaneio de devolução confirmado no armazém escolhido, com o
    /// volume dos romaneios devolvidos.
    /// </summary>
    [Fact]
    public async Task A_warehouse_return_creates_one_confirmed_return_shipment()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse), "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(DestinationWarehouse, entry.WarehouseCode);
        Assert.Equal(20_000m, entry.GrossWeight);
        Assert.Equal(20_000m, entry.NetWeight);

        // Aponta o RETORNO, não a origem: dois retornos parciais da mesma nota geram duas
        // devoluções, e o estorno de uma precisa achar exatamente a sua.
        Assert.Equal(returnInvoice.Key, entry.GeneratedByReturnInvoiceKey);
    }

    /// <summary>
    /// Dois retornos parciais ao armazém deixam DUAS devoluções, cada uma amarrada ao seu próprio
    /// documento de retorno. Amarrá-las à origem tornaria as duas indistinguíveis no estorno.
    /// </summary>
    [Fact]
    public async Task Two_partial_warehouse_returns_produce_two_distinguishable_entries()
    {
        var (invoice, r1, r2) = await SeedAsync();

        var first = await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse), "tester");

        var second = await Service().ExecuteAsync(
            Request(invoice, [r2.Key], RefusalDestination.Warehouse, DestinationWarehouse), "tester");

        var entries = await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, x => x.GeneratedByReturnInvoiceKey == first.Key);
        Assert.Single(entries, x => x.GeneratedByReturnInvoiceKey == second.Key);
    }

    /// <summary>
    /// As três chaves que o romaneio de devolução NÃO pode carregar, cada uma com estrago próprio:
    /// <c>ShipmentLoadKey</c> inflaria o volume de uma carga, <c>ShipmentReleaseKey</c> moveria
    /// saldo de liberação de COMPRA, e <c>ReturnInvoiceKey</c> é o discriminador do estorno — com
    /// ela, estornar carimbaria esta entrada como faturada.
    /// </summary>
    [Fact]
    public async Task The_return_shipment_carries_none_of_the_three_forbidden_keys()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse), "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ReturnInvoiceKey);
        Assert.Null(entry.SalesInvoiceKey);
        Assert.Null(entry.StorageAddressCode);
    }

    /// <summary>
    /// O romaneio devolvido a armazém fica <c>Invoiced</c>, e NÃO <c>Returned</c>: aquele status o
    /// tiraria das consultas de saldo, re-creditando o armazém de ORIGEM enquanto o de destino já
    /// foi creditado pelo romaneio de devolução. O mesmo grão em dois lugares.
    /// </summary>
    [Fact]
    public async Task A_warehouse_return_keeps_the_origin_shipment_debiting_its_warehouse()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse), "tester");

        var shipment = await ShipmentAsync(r1.Key);

        Assert.Equal(StorageTransactionsStatus.Invoiced, shipment.TransactionStatus);
        Assert.Equal(invoice.Key, shipment.SalesInvoiceKey);
    }

    /// <summary>
    /// Uma entrada por RETORNO, e não por romaneio: a devolução é um evento físico — o caminhão
    /// voltou com N kg ao armazém X.
    /// </summary>
    [Fact]
    public async Task Two_shipments_returning_together_produce_a_single_entry()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key, r2.Key], RefusalDestination.Warehouse, DestinationWarehouse),
            "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Equal(40_000m, entry.NetWeight);
    }

    // ─── Validações ───

    /// <summary>
    /// Documento de CARGA não se retorna por aqui: a recusa dele tem tela própria, que sabe mexer
    /// no saldo da carga. Sem este guard, a nota de carga cairia num caminho que não conhece
    /// nenhuma das regras dela.
    /// </summary>
    [Fact]
    public async Task A_shipment_load_invoice_is_refused()
    {
        var (invoice, r1, _) = await SeedAsync(shipmentLoadKey: Guid.NewGuid());

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(invoice, [r1.Key]), "tester"));

        Assert.Contains("Montagem de Carga", error.Message);
    }

    [Fact]
    public async Task A_return_without_a_reason_is_refused()
    {
        var (invoice, r1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(invoice, [r1.Key], reason: "  "), "tester"));

        Assert.Contains("motivo", error.Message);
    }

    [Fact]
    public async Task A_warehouse_return_without_a_warehouse_is_refused()
    {
        var (invoice, r1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], RefusalDestination.Warehouse), "tester"));

        Assert.Contains("armazém de destino", error.Message);
    }

    [Fact]
    public async Task A_return_without_any_shipment_is_refused()
    {
        var (invoice, _, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(invoice, []), "tester"));

        Assert.Contains("romaneio", error.Message);
    }

    /// <summary>
    /// Romaneio de outra nota não entra — é o critério largo que já sequestrou romaneio alheio na
    /// consulta de órfãos do estorno legado.
    /// </summary>
    [Fact]
    public async Task A_shipment_of_another_document_is_refused()
    {
        var (invoice, _, _) = await SeedAsync();
        var (_, otherR1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(Request(invoice, [otherR1.Key]), "tester"));

        Assert.Contains("não pertence", error.Message);
    }

    /// <summary>
    /// Nada é gravado quando a validação recusa: uma recusa não pode deixar meia devolução no
    /// banco. Por isso toda a validação corre antes da primeira escrita.
    /// </summary>
    [Fact]
    public async Task A_refused_return_leaves_nothing_behind()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], RefusalDestination.Warehouse), "tester"));

        Assert.Empty(await _db.Context.SalesInvoices
            .AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .ToListAsync());

        Assert.Equal(StorageTransactionsStatus.Invoiced, (await ShipmentAsync(r1.Key)).TransactionStatus);
    }

    // ─── Retorno parcial POR QUANTIDADE (só com destino armazém) ───

    /// <summary>
    /// Devolver parte de um romaneio credita no armazém só o que voltou, e não a carreta inteira.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_credits_only_what_came_back_to_the_warehouse()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Equal(12_000m, entry.NetWeight);
        Assert.Equal(12_000m, entry.GrossWeight);
    }

    /// <summary>
    /// A devolução leva a quantidade informada, e não o <c>NetWeight</c> do romaneio — é ela que
    /// vai para o item do documento de retorno e, por ele, para o saldo do contrato.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_return_carries_only_the_informed_quantity()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        var items = await _db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == returnInvoice.Key)
            .ToListAsync();

        Assert.Equal(12_000m, Assert.Single(items).Quantity);
    }

    /// <summary>
    /// O romaneio parcialmente devolvido continua <c>Invoiced</c> e preso à nota: o que voltou já
    /// foi creditado no armazém de destino pelo romaneio tipo 12, e o resto segue com o cliente.
    /// Soltá-lo devolveria ao pool um volume que não voltou inteiro.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_keeps_the_origin_shipment_invoiced()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        var shipment = await ShipmentAsync(r1.Key);

        Assert.Equal(StorageTransactionsStatus.Invoiced, shipment.TransactionStatus);
        Assert.Equal(invoice.Key, shipment.SalesInvoiceKey);
    }

    /// <summary>
    /// Devolvida só parte da quantidade, a origem continua Confirmada e aberta — é o que permite
    /// devolver o restante depois.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_leaves_the_origin_open_for_another_return()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        var origin = await InvoiceAsync(invoice.Key);

        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, origin.DeliveryStatus);
    }

    /// <summary>
    /// Retornos parciais por quantidade somam até fechar a nota, exatamente como os parciais por
    /// romaneio: quem decide é a quantidade devolvida de cada item, não quantos romaneios entraram.
    /// </summary>
    [Fact]
    public async Task Sequential_partial_quantity_returns_close_the_origin_when_they_add_up()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await InvoiceAsync(invoice.Key)).InvoiceStatus);

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key, r2.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [8_000m, 20_000m]),
            "tester");

        var origin = await InvoiceAsync(invoice.Key);

        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, origin.DeliveryStatus);
    }

    /// <summary>
    /// A quantidade devolvida aparece no romaneio de devolução junto do romaneio de origem: sem
    /// acumulador por romaneio, é o <c>Comments</c> que conta quanto voltou de cada carreta.
    /// </summary>
    [Fact]
    public async Task The_partial_quantity_is_recorded_against_its_shipment()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [12_000m]),
            "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Contains("R1", entry.Comments);
        Assert.Contains("12.000,000", entry.Comments);
    }

    // ─── Validações do parcial por quantidade ───

    /// <summary>
    /// Quantidade parcial não vale para "o caminhão segue viagem": ali o romaneio volta INTEIRO ao
    /// pool de faturamento, e devolvê-lo pela metade des-faturaria o volume que ficou com o
    /// cliente. Partir o romaneio em dois registros ficou deliberadamente fora.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_is_refused_when_the_truck_moves_on()
    {
        var (invoice, r1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], quantities: [12_000m]), "tester"));

        Assert.Contains("armazém", error.Message);
    }

    /// <summary>
    /// Devolver mais do que a carreta trouxe é recusado, e o erro nomeia o romaneio — o operador
    /// digita a quantidade linha a linha e precisa saber qual delas está errada.
    /// </summary>
    [Fact]
    public async Task A_quantity_greater_than_the_shipment_is_refused()
    {
        var (invoice, r1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                    quantities: [20_500m]),
                "tester"));

        Assert.Contains("R1", error.Message);
    }

    [Fact]
    public async Task A_quantity_of_zero_is_refused()
    {
        var (invoice, r1, _) = await SeedAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                    quantities: [0m]),
                "tester"));

        Assert.Contains("R1", error.Message);
    }

    /// <summary>
    /// O teto real de vários retornos é o saldo devolvível do ITEM da nota, e não o
    /// <c>NetWeight</c> de cada romaneio: sem esse limite, devolver 15.000 do R1 e depois mais
    /// 10.000 creditaria no armazém mais do que a nota vendeu.
    /// </summary>
    [Fact]
    public async Task A_quantity_above_the_returnable_balance_of_the_document_is_refused()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key, r2.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                quantities: [15_000m, 20_000m]),
            "tester");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                Request(invoice, [r1.Key], RefusalDestination.Warehouse, DestinationWarehouse,
                    quantities: [10_000m]),
                "tester"));

        Assert.Contains("saldo devolvível", error.Message);
    }
}
