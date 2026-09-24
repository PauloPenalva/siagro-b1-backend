using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): como o ramo Faturada se desdobra em Faturada, Descarregada e Concluída.
/// </summary>
/// <remarks>
/// Concluída = todos os itens das notas Normais CONFIRMADAS da carga com a entrega encerrada, sem
/// nota Pendente. A Pendente ainda não entrou na Conferência, que exige Confirmed, e as
/// Canceladas/Retornadas ficam fora como ficam fora da tela.
/// </remarks>
public class ShipmentLoadClosureTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsRecalculateInvoicedService Service() => new(
        _db, new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoad Load(decimal total = 90_000, bool isDischarged = false)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000031",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
            IsDischarged = isDischarged,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(ShipmentLoad load)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = load.TotalQuantity,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };
        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private SalesInvoice Invoice(
        ShipmentLoad load,
        decimal quantity,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        SalesInvoiceDeliveryStatus delivery = SalesInvoiceDeliveryStatus.Open)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000123",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };

        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? quantity : 0m,
            DeliveryStatus = delivery,
        });

        _db.Context.SalesInvoices.Add(invoice);
        return invoice;
    }

    /// <summary>
    /// Lê o estado REALMENTE persistido. <c>RecalculateAsync</c> só ENFILEIRA as mudanças no
    /// contexto (é quem chama que decide salvar — ver a classe sob teste); sem o
    /// <c>SaveChangesAsync</c> aqui, o InMemory provider devolveria pelo <c>AsNoTracking</c> o
    /// valor antigo ainda no "banco", porque uma consulta sem tracking não usa o identity map
    /// que devolveria a instância já mutada em memória.
    /// </summary>
    private async Task<ShipmentLoad> SavedAsync()
    {
        await _db.Context.SaveChangesAsync();
        return await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Invoiced, false, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(ShipmentLoadStatus.Invoiced, true, false, ShipmentLoadStatus.Discharged)]
    [InlineData(ShipmentLoadStatus.Invoiced, false, true, ShipmentLoadStatus.Completed)]
    [InlineData(ShipmentLoadStatus.Invoiced, true, true, ShipmentLoadStatus.Completed)]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, true, true, ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(ShipmentLoadStatus.Returned, true, true, ShipmentLoadStatus.Returned)]
    [InlineData(ShipmentLoadStatus.InTransshipment, true, true, ShipmentLoadStatus.InTransshipment)]
    [InlineData(ShipmentLoadStatus.Open, true, true, ShipmentLoadStatus.Open)]
    public void Closure_only_refines_the_invoiced_branch(
        ShipmentLoadStatus baseStatus, bool isDischarged, bool allClosed, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected,
            ShipmentLoadsRecalculateInvoicedService.ResolveClosure(baseStatus, isDischarged, allClosed));
    }

    [Fact]
    public async Task All_deliveries_closed_completes_the_load_and_keeps_the_shipments_invoiced()
    {
        var load = Load();
        var shipment = Shipment(load);
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    /// <summary>
    /// O "Recalcular Saldo" da tela é o remédio da carga histórica cuja Conferência já estava toda
    /// encerrada antes do deploy. A transição que ele faz fica no log, assinada por quem clicou,
    /// como nos outros caminhos que mudam a situação.
    /// </summary>
    [Fact]
    public async Task Recalculating_on_request_logs_the_status_change_signed_by_the_user()
    {
        var load = Load();
        load.Status = ShipmentLoadStatus.Invoiced;
        load.InvoicedQuantity = 90_000;
        Shipment(load).TransactionStatus = StorageTransactionsStatus.Invoiced;
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Completed, saved.Status);
        Assert.Equal("tester", saved.UpdatedBy);

        var log = await _db.Context.ShipmentLoadsChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Concluída", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
    }

    [Fact]
    public async Task Recalculating_on_request_without_a_status_change_writes_no_log()
    {
        var load = Load();
        load.Status = ShipmentLoadStatus.Invoiced;
        load.InvoicedQuantity = 90_000;
        Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    [Fact]
    public async Task One_open_delivery_keeps_the_load_invoiced()
    {
        var load = Load();
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task The_manual_mark_turns_invoiced_into_discharged_and_keeps_the_shipments_invoiced()
    {
        var load = Load(isDischarged: true);
        var shipment = Shipment(load);
        Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Discharged, saved.Status);
        Assert.True(saved.IsDischarged);
        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    /// <summary>Review Focus 2: a Pendente ainda não entrou na Conferência.</summary>
    [Fact]
    public async Task A_pending_invoice_prevents_completion_even_with_the_rest_closed()
    {
        var load = Load();
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000, InvoiceStatus.Pending);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Cancelled_and_returned_invoices_do_not_block_completion()
    {
        var load = Load(total: 100_000);
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 10_000, InvoiceStatus.Returned);   // Returned continua consumindo
        Invoice(load, 5_000, InvoiceStatus.Cancelled);   // Cancelled não consome
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Leaving_invoiced_clears_the_manual_mark()
    {
        var load = Load(isDischarged: true);
        Invoice(load, 90_000, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        Assert.False(saved.IsDischarged);
    }

    /// <summary>
    /// Review Focus 1: o recálculo roda DENTRO de transações alheias, às vezes antes do flush
    /// que tornaria a mudança do item visível no banco. A regra precisa ler o estado RASTREADO.
    /// </summary>
    [Fact]
    public async Task An_unsaved_reopen_of_a_tracked_item_is_seen()
    {
        var load = Load();
        var invoice = Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        invoice.Items.Single().DeliveryStatus = SalesInvoiceDeliveryStatus.Open; // sem SaveChanges

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, excludedInvoiceKeys: null);

        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
    }

    [Fact]
    public async Task An_unsaved_status_change_of_a_tracked_invoice_is_seen()
    {
        var load = Load(total: 100_000);
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        var returned = Invoice(load, 10_000, InvoiceStatus.Returned);
        await _db.Context.SaveChangesAsync();

        returned.InvoiceStatus = InvoiceStatus.Confirmed; // origem restaurada, item ainda Open

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, excludedInvoiceKeys: null);

        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
    }

    [Fact]
    public async Task A_load_without_confirmed_invoices_is_never_completed()
    {
        Assert.False(await ShipmentLoadsRecalculateInvoicedService.AreAllDeliveriesClosedAsync(
            _db.Context, Guid.NewGuid(), excludedInvoiceKeys: null));
    }
}
