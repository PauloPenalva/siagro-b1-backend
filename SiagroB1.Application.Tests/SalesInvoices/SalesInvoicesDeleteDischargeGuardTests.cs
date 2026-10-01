using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1171: excluir a nota de saída — ou só uma linha dela — com ticket de descarga registrado.
/// </summary>
/// <remarks>
/// As FKs de SHIPMENT_LOAD_DISCHARGE_ITEMS para a nota e para a linha são <c>NoAction</c> de
/// propósito: o ticket é a evidência física que libera o pagamento do frete e não pode ser levado
/// embora junto com o documento. Sem guard, o banco real devolve erro 547, a transação rola atrás e
/// o usuário recebe um 500 de corpo vazio — o caminho é real porque o ticket nasce em nota
/// Confirmada e o estorno da confirmação a devolve a <c>Pending</c>, que é a nota que o delete aceita.
/// <para>
/// ⚠️ O provider InMemory NÃO aplica FK: sem o guard estes testes passariam em verde e a falha só
/// apareceria em produção. É o guard, e não o banco, que este arquivo verifica.
/// </para>
/// </remarks>
public class SalesInvoicesDeleteDischargeGuardTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesDeleteService InvoiceDeleteService() => new(
        _db,
        new ShipmentLoadsBalanceHookService(
            _db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
        NullLogger<SalesInvoicesDeleteService>.Instance);

    private SalesInvoicesItemsDeleteService ItemDeleteService() => new(
        _db, NullLogger<SalesInvoicesItemsDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    /// <summary>
    /// Carga faturada com uma nota AINDA PENDENTE — a combinação que o chamado expõe: a nota
    /// pendente aceita ticket e é a única que o delete aceita.
    /// </summary>
    private async Task SeedAsync()
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = ShipmentLoadStatus.Invoiced,
            TotalQuantity = 40000m,
        };

        _invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            ShipmentLoadKey = _load.Key,
            InvoiceStatus = InvoiceStatus.Pending,
        };

        _item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = _invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40000m,
        };

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesInvoices.Add(_invoice);
        _db.Context.SalesInvoicesItems.Add(_item);
        await _db.Context.SaveChangesAsync();
    }

    /// <summary>
    /// Ticket gravado direto, sobre a nota PENDENTE. Pela tela ele só nasce em nota Confirmada; o
    /// caminho real até aqui é estornar a confirmação depois do ticket.
    /// </summary>
    private async Task RegisterTicketAsync()
    {
        var discharge = new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = _load.Key,
            TicketNumber = "T-1",
            DischargeDate = new DateTime(2026, 9, 17),
            DischargedQuantity = 39500m,
        };

        discharge.Items.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = _invoice.Key,
            SalesInvoiceItemKey = _item.Key!.Value,
            Quantity = 39500m,
        });

        _db.Context.ShipmentLoadsDischarges.Add(discharge);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Deleting_the_invoice_with_a_discharge_ticket_is_refused()
    {
        await SeedAsync();
        await RegisterTicketAsync();

        var exception = await Assert.ThrowsAsync<DefaultException>(
            () => InvoiceDeleteService().ExecuteAsync(_invoice.Key, "paulo"));

        Assert.Contains("ticket de descarga", exception.Message);

        // O ticket continua lá: excluí-lo junto destruiria a evidência física do frete.
        Assert.Single(_db.Context.ShipmentLoadsDischarges);
        Assert.Single(_db.Context.ShipmentLoadsDischargesItems);
        Assert.NotNull(_db.Context.SalesInvoices.FirstOrDefault(x => x.Key == _invoice.Key));
    }

    [Fact]
    public async Task Deleting_the_invoice_item_with_a_discharge_ticket_is_refused()
    {
        await SeedAsync();
        await RegisterTicketAsync();

        var exception = await Assert.ThrowsAsync<DefaultException>(
            () => ItemDeleteService().ExecuteAsync(_item.Key!.Value));

        Assert.Contains("ticket de descarga", exception.Message);

        Assert.Single(_db.Context.ShipmentLoadsDischarges);
        Assert.Single(_db.Context.ShipmentLoadsDischargesItems);
        Assert.NotNull(_db.Context.SalesInvoicesItems.FirstOrDefault(x => x.Key == _item.Key));
    }

    /// <summary>
    /// O guard é estreito: sem ticket, os dois deletes continuam funcionando como antes.
    /// </summary>
    [Fact]
    public async Task Without_a_discharge_ticket_both_deletes_still_work()
    {
        await SeedAsync();

        Assert.True(await ItemDeleteService().ExecuteAsync(_item.Key!.Value));
        Assert.True(await InvoiceDeleteService().ExecuteAsync(_invoice.Key, "paulo"));

        Assert.Empty(_db.Context.SalesInvoicesItems);
        Assert.Empty(_db.Context.SalesInvoices);
    }
}
