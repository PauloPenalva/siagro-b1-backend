using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Desvinculação de romaneios. O guard daqui protege a invariante I2 pelo lado que o guard de
/// faturamento não vigia: ele impede a soma das notas de ultrapassar o volume, mas nada pode
/// contra o VOLUME ENCOLHER por baixo de notas já emitidas.
/// </summary>
public class ShipmentLoadsDetachTransactionsServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsDetachTransactionsService Service() => new(
        _db,
        new ShipmentLoadsCompositionGuardService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(
        ShipmentLoadStatus status = ShipmentLoadStatus.Open,
        decimal totalQuantity = 60_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = totalQuantity,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(
        Guid? loadKey,
        string code,
        decimal grossWeight = 30_000,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = status,
            ShipmentLoadKey = loadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// A nota nasce COM item: a trava decide pelo volume consumido, e uma nota sem linha não
    /// consome nada. Um documento sem item nunca existe no fluxo real.
    /// </summary>
    private SalesInvoice Invoice(
        ShipmentLoad load,
        InvoiceStatus status,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        decimal quantity = 30_000m,
        Guid? originKey = null,
        string invoiceNumber = "000001")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            ShipmentLoadKey = load.Key,
            InvoiceStatus = status,
            InvoiceType = type,
            SalesInvoiceOriginKey = originKey,
            CardCode = "C001",
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
        });

        _db.Context.SalesInvoices.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task Detaching_lowers_the_total_and_frees_the_shipment()
    {
        var load = Load();
        var a = Shipment(load.Key, "R1", 30_000);
        Shipment(load.Key, "R2", 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(30_000m, saved.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);

        var freed = await _db.Context.StorageTransactions.SingleAsync(x => x.Code == "R1");
        Assert.Null(freed.ShipmentLoadKey);
        // Voltar para Confirmed é o que devolve o romaneio à lista de disponíveis.
        Assert.Equal(StorageTransactionsStatus.Confirmed, freed.TransactionStatus);
    }

    /// <summary>
    /// Esvaziar a carga a devolve ao planejamento — é o ciclo completo da regra de status.
    /// </summary>
    [Fact]
    public async Task Emptying_the_load_sends_it_back_to_planned()
    {
        var load = Load();
        var a = Shipment(load.Key, "R1", 30_000);
        var b = Shipment(load.Key, "R2", 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key, b.Key], "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(decimal.Zero, saved.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Planned, saved.Status);
    }

    [Fact]
    public async Task Detaching_records_a_movement_with_a_negative_quantity()
    {
        var load = Load();
        var a = Shipment(load.Key, "R1", 30_000);
        Shipment(load.Key, "R2", 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.TransactionsDetached, movement.MovementType);
        // Assinada: sai volume, então é negativa.
        Assert.Equal(-30_000m, movement.Quantity);
        Assert.Contains("R1", movement.Description);
    }

    /// <summary>
    /// O guard central: nota viva trava a composição, seja qual for o status da carga. A
    /// checagem é pelas NOTAS e não pelo status porque status é derivado e oscila.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Confirmed)]
    [InlineData(InvoiceStatus.Returned)]
    public async Task Refuses_to_detach_when_a_live_invoice_exists(InvoiceStatus status)
    {
        var load = Load(ShipmentLoadStatus.PartiallyInvoiced);
        var a = Shipment(load.Key, "R1", 30_000);
        Invoice(load, status);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], "tester"));

        Assert.Contains("CG000001", error.Message);
        // Nada foi gravado: o romaneio continua na carga.
        Assert.Equal(load.Key, (await _db.Context.StorageTransactions.SingleAsync()).ShipmentLoadKey);
    }

    /// <summary>
    /// Recusa TOTAL para refaturamento: a devolução confirmada zera o consumo e a composição
    /// volta a poder mudar, apesar de continuarem existindo DOIS documentos vivos (origem
    /// <c>Returned</c> + retorno <c>Confirmed</c>).
    /// </summary>
    [Fact]
    public async Task A_fully_returned_load_can_be_detached_again()
    {
        var load = Load(ShipmentLoadStatus.PartiallyInvoiced);
        var a = Shipment(load.Key, "R1", 30_000);
        var origin = Invoice(load, InvoiceStatus.Returned);
        Invoice(load, InvoiceStatus.Confirmed, SalesInvoiceType.Return,
            originKey: origin.Key, invoiceNumber: "000002");
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], "tester");

        Assert.Null((await _db.Context.StorageTransactions.SingleAsync()).ShipmentLoadKey);
    }

    /// <summary>
    /// Devolução ao ARMAZÉM continua travando: o grão já está creditado no armazém de destino, e
    /// soltar o romaneio o devolveria à Montagem para ser faturado de novo.
    /// </summary>
    [Fact]
    public async Task A_load_returned_to_a_warehouse_cannot_be_detached()
    {
        var load = Load(ShipmentLoadStatus.PartiallyInvoiced);
        var a = Shipment(load.Key, "R1", 30_000);

        var refusalReturn = Shipment(load.Key, "RM000777", 30_000);
        refusalReturn.TransactionType = StorageTransactionType.SalesShipmentReturn;
        refusalReturn.ShipmentLoadKey = null;
        refusalReturn.RefusedFromShipmentLoadKey = load.Key;

        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], "tester"));

        Assert.Contains("devolvido(s) ao armazém", error.Message);
        Assert.Equal(load.Key, (await _db.Context.StorageTransactions
            .SingleAsync(x => x.Key == a.Key)).ShipmentLoadKey);
    }

    [Fact]
    public async Task A_cancelled_invoice_does_not_block_detaching()
    {
        var load = Load();
        var a = Shipment(load.Key, "R1", 30_000);
        Invoice(load, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], "tester");

        Assert.Null((await _db.Context.StorageTransactions.SingleAsync()).ShipmentLoadKey);
    }

    [Fact]
    public async Task Refuses_a_shipment_that_belongs_to_another_load()
    {
        var load = Load();
        var other = Load();
        other.Code = "CG000099";
        var stranger = Shipment(other.Key, "R9", 10_000);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [stranger.Key], "tester"));

        Assert.Contains("R9", error.Message);
    }

    [Fact]
    public async Task Refuses_to_detach_from_a_cancelled_load()
    {
        var load = Load(ShipmentLoadStatus.Cancelled);
        var a = Shipment(load.Key, "R1");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], "tester"));

        Assert.Contains("cancelada", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cancelado e devolvido são estados DO ROMANEIO, não projeção da carga — a desvinculação
    /// solta o vínculo mas não os reescreve.
    /// </summary>
    [Fact]
    public async Task Does_not_rewrite_a_cancelled_shipment_status()
    {
        var load = Load();
        var a = Shipment(load.Key, "R1", 30_000, StorageTransactionsStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], "tester");

        var freed = await _db.Context.StorageTransactions.SingleAsync();
        Assert.Null(freed.ShipmentLoadKey);
        Assert.Equal(StorageTransactionsStatus.Cancelled, freed.TransactionStatus);
    }

    [Fact]
    public async Task Refuses_an_empty_selection()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [], "tester"));

        Assert.Contains("romaneio", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
