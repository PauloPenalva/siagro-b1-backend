using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registro de descarga: gravação, guards e as linhas que cada operação deixa no log da carga.
/// </summary>
public class ShipmentLoadDischargesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Recalculate() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadDischargesCreateService CreateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesCreateService>.Instance);

    private ShipmentLoadDischargesUpdateService UpdateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesUpdateService>.Instance);

    private ShipmentLoadDischargesDeleteService DeleteService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    private async Task SeedAsync(ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        InvoiceStatus invoiceStatus = InvoiceStatus.Confirmed)
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
            Status = status,
            TotalQuantity = 40000m,
        };

        _invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            ShipmentLoadKey = _load.Key,
            InvoiceStatus = invoiceStatus,
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

    private Task<ShipmentLoadDischarge> CreateAsync(decimal quantity = 39500m, string ticket = "T-1") =>
        CreateService().ExecuteAsync(
            _load.Key, _invoice.Key, _item.Key!.Value,
            ticket, new DateTime(2026, 9, 17), quantity, "descarga na trading", null, "paulo");

    private List<ShipmentLoadChangeLog> LogsOf() =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == _load.Key).ToList();

    [Fact]
    public async Task Create_stores_the_ticket_sums_it_and_logs_it()
    {
        await SeedAsync();

        var discharge = await CreateAsync();

        Assert.Equal("T-1", discharge.TicketNumber);
        Assert.Equal("paulo", discharge.CreatedBy);
        Assert.NotNull(discharge.CreatedAt);
        Assert.Equal(39500m, _item.TicketDeliveredQuantity);
        Assert.Equal(39500m, _load.DischargedQuantity);

        var log = Assert.Single(LogsOf());
        Assert.Equal(ShipmentLoadChangeLogFields.Discharge, log.Field);
        Assert.Null(log.OldValue);
        Assert.Contains("T-1", log.NewValue);
    }

    /// <summary>As três operações que o ticket conhece, para a Theory da regra central.</summary>
    public enum DischargeOperation
    {
        Create,
        Update,
        Delete,
    }

    /// <summary>
    /// ⚠️ O TESTE QUE GUARDA A REGRA CENTRAL do GAC-1171, e a razão de ser deste arquivo.
    ///
    /// Registrar, alterar ou excluir um ticket de descarga NÃO pode mexer na conferência de
    /// entrega (<c>DeliveredQuantity</c>, <c>QuantityLoss</c>, <c>DeliveryStatus</c>) nem no saldo
    /// do contrato de venda ou da liberação de entrega — com a entrega ABERTA ou ENCERRADA.
    ///
    /// O ticket é o peso da balança do destino, segundo um documento que pode ter sido adulterado
    /// pelo transportador. Deixá-lo mover saldo transformaria esse número no padrão da
    /// conferência. Quem move saldo continua sendo um único ato: o encerramento da conferência.
    ///
    /// Vale para as seis combinações porque a entrega encerrada é justamente o caso em que o
    /// contrato JÁ consumiu o líquido — um recálculo disparado por engano aqui reescreveria um
    /// saldo que ninguém pediu para mexer.
    /// </summary>
    [Theory]
    [InlineData(DischargeOperation.Create, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Create, SalesInvoiceDeliveryStatus.Closed)]
    [InlineData(DischargeOperation.Update, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Update, SalesInvoiceDeliveryStatus.Closed)]
    [InlineData(DischargeOperation.Delete, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Delete, SalesInvoiceDeliveryStatus.Closed)]
    public async Task Ticket_leaves_the_reconciliation_and_the_balances_untouched(
        DischargeOperation operation, SalesInvoiceDeliveryStatus deliveryStatus)
    {
        await SeedAsync();
        var (contract, release) = await SeedContractAndReleaseAsync();

        _item.DeliveredQuantity = 38000m;
        _item.QuantityLoss = 250m;
        _item.DeliveryStatus = deliveryStatus;
        await _db.Context.SaveChangesAsync();

        // Update e Delete precisam de um ticket já gravado. O snapshot é tirado DEPOIS dele, para
        // que a operação sob teste seja a única coisa que aconteceu entre o "antes" e o "depois".
        var discharge = operation == DischargeOperation.Create ? null : await CreateAsync();

        var deliveredBefore = _item.DeliveredQuantity;
        var lossBefore = _item.QuantityLoss;
        var deliveryStatusBefore = _item.DeliveryStatus;
        var allocatedBefore = contract.AllocatedVolume;
        var availableBefore = contract.AvaiableVolume;
        var totalReleasesBefore = contract.TotalShipmentReleases;
        var shippedBefore = release.ShippedQuantity;
        var releaseAvailableBefore = release.AvailableQuantity;

        var expectedTicketSum = operation switch
        {
            DischargeOperation.Create => 39500m,
            DischargeOperation.Update => 40100m,
            _ => 0m,
        };

        switch (operation)
        {
            case DischargeOperation.Create:
                await CreateAsync();
                break;
            case DischargeOperation.Update:
                await UpdateService().ExecuteAsync(
                    discharge!.Key!.Value, "T-1A", new DateTime(2026, 9, 18), 40100m, "corrigido",
                    "paulo");
                break;
            default:
                await DeleteService().ExecuteAsync(discharge!.Key!.Value, "paulo");
                break;
        }

        // A operação realmente rodou: a soma do ticket — e SÓ ela — se moveu.
        Assert.Equal(expectedTicketSum, _item.TicketDeliveredQuantity);
        Assert.Equal(expectedTicketSum, _load.DischargedQuantity);

        // Conferência de entrega intacta.
        Assert.Equal(deliveredBefore, _item.DeliveredQuantity);
        Assert.Equal(lossBefore, _item.QuantityLoss);
        Assert.Equal(deliveryStatusBefore, _item.DeliveryStatus);

        // Saldo do contrato intacto (AllocatedVolume é o persistido; AvaiableVolume deriva dele).
        Assert.Equal(allocatedBefore, contract.AllocatedVolume);
        Assert.Equal(availableBefore, contract.AvaiableVolume);
        Assert.Equal(totalReleasesBefore, contract.TotalShipmentReleases);

        // Saldo da liberação de entrega intacto.
        Assert.Equal(shippedBefore, release.ShippedQuantity);
        Assert.Equal(releaseAvailableBefore, release.AvailableQuantity);
    }

    /// <summary>
    /// Contrato de venda aprovado com uma liberação de entrega já parcialmente embarcada, ligado à
    /// linha da nota. É contra estes números que a regra central afirma "nada se moveu".
    /// </summary>
    private async Task<(SalesContract Contract, SalesShipmentRelease Release)>
        SeedContractAndReleaseAsync()
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-1171",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100000m,
            AllocatedVolume = 40000m,
            Status = ContractStatus.Approved,
        };

        var release = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 60000m,
            ShippedQuantity = 40000m,
            Status = ReleaseStatus.Actived,
        };

        contract.SalesShipmentReleases.Add(release);
        _db.Context.SalesContracts.Add(contract);

        _item.SalesContractKey = contract.Key;
        _item.SalesShipmentReleaseKey = release.Key;

        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    [Fact]
    public async Task Update_changes_the_weight_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1A", new DateTime(2026, 9, 18), 40100m, "corrigido", "paulo");

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(40100m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.UpdatedBy);
        Assert.Equal(40100m, _item.TicketDeliveredQuantity);
        Assert.Equal(2, LogsOf().Count);
    }

    [Fact]
    public async Task Delete_removes_the_ticket_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _load.DischargedQuantity);

        var deletionLog = LogsOf().Last();
        Assert.Contains("T-1", deletionLog.OldValue);
        Assert.Null(deletionLog.NewValue);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    [InlineData(ShipmentLoadStatus.Returned)]
    public async Task Frozen_load_refuses_the_three_operations(ShipmentLoadStatus status)
    {
        await SeedAsync(ShipmentLoadStatus.Invoiced);
        var discharge = await CreateAsync();

        _load.Status = status;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(1000m, "T-2"));
        await Assert.ThrowsAsync<DefaultException>(() => UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1", new DateTime(2026, 9, 17), 100m, null, "paulo"));
        await Assert.ThrowsAsync<DefaultException>(() =>
            DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo"));
    }

    [Fact]
    public async Task Cancelled_invoice_refuses_a_new_ticket()
    {
        await SeedAsync(invoiceStatus: InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync());
    }

    [Fact]
    public async Task Item_from_another_invoice_is_refused()
    {
        await SeedAsync();

        var strangerItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = Guid.NewGuid(),
            ItemCode = "MILHO",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoicesItems.Add(strangerItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key, _invoice.Key, strangerItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Fact]
    public async Task Invoice_from_another_load_is_refused()
    {
        await SeedAsync();

        var otherInvoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C002",
            ShipmentLoadKey = Guid.NewGuid(),
        };
        var otherItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = otherInvoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoices.Add(otherInvoice);
        _db.Context.SalesInvoicesItems.Add(otherItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key, otherInvoice.Key, otherItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_weight_is_refused(decimal quantity)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(quantity, "T-3"));
    }

    [Fact]
    public async Task Blank_ticket_number_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(100m, "   "));
    }
}
