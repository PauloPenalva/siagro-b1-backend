using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registro de descarga RATEADO (GAC-1171, rateio): o ticket e as parcelas, os guards, as somas por
/// linha e por carga, a Descarregada automática e o log.
/// </summary>
public class ShipmentLoadDischargesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Recalculate() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);
    private ShipmentLoadsClosureHookService ClosureHook() => new(_db.Context, ChangeLog());

    private ShipmentLoadDischargesCreateService CreateService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesCreateService>.Instance);

    private ShipmentLoadDischargesUpdateService UpdateService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesUpdateService>.Instance);

    private ShipmentLoadDischargesDeleteService DeleteService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;
    private SalesInvoice _other = null!;
    private SalesInvoiceItem _otherItem = null!;

    /// <summary>
    /// Um caminhão de 40 t faturado em DUAS notas Normais de 20 t (000100 e 000101) — o caso do
    /// pedido: um ticket só, rateado entre as notas.
    /// </summary>
    private async Task SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        InvoiceStatus firstInvoiceStatus = InvoiceStatus.Confirmed,
        decimal totalQuantity = 40_000m)
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
            TotalQuantity = totalQuantity,
            InvoicedQuantity = 40_000m,
        };
        _db.Context.ShipmentLoads.Add(_load);

        (_invoice, _item) = AddInvoice("000100", 20_000m, firstInvoiceStatus);
        (_other, _otherItem) = AddInvoice("000101", 20_000m, InvoiceStatus.Confirmed);

        await _db.Context.SaveChangesAsync();
    }

    private (SalesInvoice Invoice, SalesInvoiceItem Item) AddInvoice(
        string number,
        decimal quantity,
        InvoiceStatus status,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        Guid? loadKey = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = number,
            ShipmentLoadKey = loadKey ?? _load.Key,
            InvoiceStatus = status,
            InvoiceType = type,
        };

        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
        };

        invoice.Items.Add(item);
        _db.Context.SalesInvoices.Add(invoice);

        return (invoice, item);
    }

    private static ShipmentLoadDischargeLine Line(SalesInvoiceItem item, decimal quantity) =>
        new(item.Key!.Value, quantity);

    /// <summary>O ticket do caso comum: 39,5 t rateadas meio a meio entre as duas notas.</summary>
    private ShipmentLoadDischargeLine[] HalfAndHalf() =>
        [Line(_item, 19_750m), Line(_otherItem, 19_750m)];

    private Task<ShipmentLoadDischarge> CreateAsync(
        decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines, string ticket = "T-1") =>
        CreateService().ExecuteAsync(
            _load.Key, ticket, new DateTime(2026, 9, 17), quantity, lines,
            "descarga na trading", null, "paulo");

    private Task UpdateAsync(
        ShipmentLoadDischarge discharge, decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines) =>
        UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1A", new DateTime(2026, 9, 18), quantity, lines, "corrigido", "paulo");

    private List<ShipmentLoadChangeLog> LogsOf(string field) =>
        _db.Context.ShipmentLoadsChangeLogs
            .Where(l => l.ShipmentLoadKey == _load.Key && l.Field == field)
            .ToList();

    [Fact]
    public async Task Create_stores_the_ticket_and_its_distribution_and_sums_each_line()
    {
        await SeedAsync();

        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal("T-1", discharge.TicketNumber);
        Assert.Equal(39_500m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.CreatedBy);
        Assert.Equal(2, discharge.Items.Count);
        Assert.All(discharge.Items, line => Assert.Equal(19_750m, line.Quantity));
        Assert.Contains(discharge.Items, line => line.SalesInvoiceKey == _invoice.Key);
        Assert.Contains(discharge.Items, line => line.SalesInvoiceKey == _other.Key);

        Assert.Equal(19_750m, _item.TicketDeliveredQuantity);
        Assert.Equal(19_750m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(39_500m, _load.DischargedQuantity);

        var log = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Discharge));
        Assert.Null(log.OldValue);
        Assert.Equal("T-1 — 39.500,000 (000100: 19.750,000; 000101: 19.750,000)", log.NewValue);
    }

    [Fact]
    public async Task The_ticket_that_completes_every_line_makes_the_load_discharged_and_logs_who_did_it()
    {
        await SeedAsync();

        await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal(ShipmentLoadStatus.Discharged, _load.Status);
        var status = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Status));
        Assert.Equal("Faturada", status.OldValue);
        Assert.Equal("Descarregada", status.NewValue);
        Assert.Equal("paulo", status.ChangedBy);
    }

    [Fact]
    public async Task A_ticket_on_one_of_two_invoices_keeps_the_load_invoiced_until_the_second_one()
    {
        await SeedAsync();

        await CreateAsync(20_000m, [Line(_item, 20_000m)], "T-1");

        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);
        Assert.Empty(LogsOf(ShipmentLoadChangeLogFields.Status));

        await CreateAsync(19_800m, [Line(_otherItem, 19_800m)], "T-2");

        Assert.Equal(ShipmentLoadStatus.Discharged, _load.Status);
    }

    [Fact]
    public async Task Deleting_the_ticket_zeroes_the_sums_and_returns_the_load_to_invoiced()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Empty(_db.Context.ShipmentLoadsDischargesItems);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(0m, _load.DischargedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);

        var deletion = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Discharge), l => l.NewValue is null);
        Assert.Contains("000101: 19.750,000", deletion.OldValue);
    }

    /// <summary>As três operações que o ticket conhece, para a Theory da regra central.</summary>
    public enum DischargeOperation
    {
        Create,
        Update,
        Delete,
    }

    /// <summary>
    /// ⚠️ O TESTE QUE GUARDA A REGRA CENTRAL do GAC-1171. Registrar, alterar ou excluir um ticket NÃO
    /// pode mexer na conferência de entrega nem no saldo do contrato ou da liberação — com a entrega
    /// ABERTA ou ENCERRADA. Continua valendo com o rateio e com o recálculo de status que o ticket
    /// agora dispara.
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

        _item.DeliveredQuantity = 19_000m;
        _item.QuantityLoss = 250m;
        _item.DeliveryStatus = deliveryStatus;
        await _db.Context.SaveChangesAsync();

        var discharge = operation == DischargeOperation.Create ? null : await CreateAsync(39_500m, HalfAndHalf());

        var deliveredBefore = _item.DeliveredQuantity;
        var lossBefore = _item.QuantityLoss;
        var deliveryStatusBefore = _item.DeliveryStatus;
        var allocatedBefore = contract.AllocatedVolume;
        var availableBefore = contract.AvaiableVolume;
        var totalReleasesBefore = contract.TotalShipmentReleases;
        var shippedBefore = release.ShippedQuantity;
        var releaseAvailableBefore = release.AvailableQuantity;

        var (expectedLine, expectedLoad) = operation switch
        {
            DischargeOperation.Create => (19_750m, 39_500m),
            DischargeOperation.Update => (40_100m, 40_100m),
            _ => (0m, 0m),
        };

        switch (operation)
        {
            case DischargeOperation.Create:
                await CreateAsync(39_500m, HalfAndHalf());
                break;
            case DischargeOperation.Update:
                await UpdateAsync(discharge!, 40_100m, [Line(_item, 40_100m)]);
                break;
            default:
                await DeleteService().ExecuteAsync(discharge!.Key!.Value, "paulo");
                break;
        }

        Assert.Equal(expectedLine, _item.TicketDeliveredQuantity);
        Assert.Equal(expectedLoad, _load.DischargedQuantity);

        Assert.Equal(deliveredBefore, _item.DeliveredQuantity);
        Assert.Equal(lossBefore, _item.QuantityLoss);
        Assert.Equal(deliveryStatusBefore, _item.DeliveryStatus);

        Assert.Equal(allocatedBefore, contract.AllocatedVolume);
        Assert.Equal(availableBefore, contract.AvaiableVolume);
        Assert.Equal(totalReleasesBefore, contract.TotalShipmentReleases);

        Assert.Equal(shippedBefore, release.ShippedQuantity);
        Assert.Equal(releaseAvailableBefore, release.AvailableQuantity);
    }

    private async Task<(SalesContract Contract, SalesShipmentRelease Release)> SeedContractAndReleaseAsync()
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
    public async Task Update_redistributes_and_resums_the_line_that_left_the_distribution()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        await UpdateAsync(discharge, 40_100m, [Line(_item, 40_100m)]);

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(40_100m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.UpdatedBy);
        Assert.Equal(40_100m, Assert.Single(discharge.Items).Quantity);
        Assert.Equal(40_100m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(40_100m, _load.DischargedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);
    }

    /// <summary>
    /// Review Focus 1: uma nota do rateio foi cancelada depois do ticket. A tela não a oferece mais,
    /// e alterar o ticket sem ela precisa tirar a parcela e zerar a soma daquela linha.
    /// </summary>
    [Fact]
    public async Task Editing_after_an_invoice_was_cancelled_drops_its_line_and_zeroes_its_sum()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        await UpdateAsync(discharge, 39_500m, [Line(_item, 39_500m)]);

        Assert.Equal(_item.Key!.Value, Assert.Single(discharge.Items).SalesInvoiceItemKey);
        Assert.Equal(39_500m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task Editing_with_the_line_of_a_cancelled_invoice_is_refused()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => UpdateAsync(discharge, 39_500m, HalfAndHalf()));

        Assert.Contains("000101", ex.Message);
    }

    /// <summary>
    /// Revisão final: a nota do rateio teve a confirmação ESTORNADA depois do ticket (para corrigir
    /// dado fiscal, por exemplo). Pendente é passageiro, ao contrário de Cancelada: corrigir o número do
    /// ticket não pode obrigar o usuário a jogar a parcela dela em outra nota.
    /// </summary>
    [Fact]
    public async Task Editing_after_an_invoice_went_back_to_pending_keeps_its_unchanged_share()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Pending;
        await _db.Context.SaveChangesAsync();

        await UpdateAsync(discharge, 39_500m, HalfAndHalf());

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(2, discharge.Items.Count);
        Assert.Equal(19_750m, _item.TicketDeliveredQuantity);
        Assert.Equal(19_750m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);

        // Confirmada de novo, a carga volta a Descarregada: a parcela não se perdeu na edição.
        _other.InvoiceStatus = InvoiceStatus.Confirmed;
        await ClosureHook().ApplyAsync(_load.Key, "paulo");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(ShipmentLoadStatus.Discharged, _load.Status);
    }

    [Fact]
    public async Task Changing_the_saved_share_of_a_pending_invoice_is_refused()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Pending;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => UpdateAsync(discharge, 39_500m, [Line(_item, 19_500m), Line(_otherItem, 20_000m)]));

        Assert.Equal(
            "O documento de saída 000101 não está confirmado: a parcela gravada (19.750,000) não pode ser " +
            "alterada. Confirme o documento de novo para mudar o rateio.",
            ex.Message);
        Assert.Equal(19_750m, _otherItem.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task Editing_cannot_add_a_share_on_a_pending_invoice()
    {
        await SeedAsync();
        var discharge = await CreateAsync(20_000m, [Line(_item, 20_000m)]);

        _other.InvoiceStatus = InvoiceStatus.Pending;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => UpdateAsync(discharge, 30_000m, [Line(_item, 20_000m), Line(_otherItem, 10_000m)]));

        Assert.Equal(
            "O documento de saída 000101 não está confirmado. Só documento confirmado recebe descarga.",
            ex.Message);
    }

    [Fact]
    public async Task A_distribution_that_does_not_close_is_refused_and_nothing_is_saved()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(39_500m, [Line(_item, 19_750m), Line(_otherItem, 19_000m)]));

        Assert.Contains("não fecha", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task A_line_of_another_load_is_refused()
    {
        await SeedAsync();
        var (_, stranger) = AddInvoice("000999", 10_000m, InvoiceStatus.Confirmed, loadKey: Guid.NewGuid());
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(10_000m, [Line(stranger, 10_000m)]));

        Assert.Equal("O documento de saída 000999 não pertence a esta carga.", ex.Message);
    }

    [Fact]
    public async Task A_pending_invoice_line_is_refused()
    {
        await SeedAsync(firstInvoiceStatus: InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(20_000m, [Line(_item, 20_000m)]));

        Assert.Equal(
            "O documento de saída 000100 não está confirmado. Só documento confirmado recebe descarga.",
            ex.Message);
    }

    [Fact]
    public async Task A_return_invoice_line_is_refused()
    {
        await SeedAsync();
        var (_, returnItem) = AddInvoice("000200", 5_000m, InvoiceStatus.Confirmed, SalesInvoiceType.Return);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(5_000m, [Line(returnItem, 5_000m)]));

        Assert.Equal("O documento 000200 é de devolução e não recebe descarga.", ex.Message);
    }

    [Fact]
    public async Task A_line_returned_in_full_is_refused()
    {
        await SeedAsync();
        _item.ReturnedQuantity = 20_000m;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(20_000m, [Line(_item, 20_000m)]));

        Assert.Equal(
            "O documento de saída 000100 foi devolvido por inteiro e não recebe descarga.",
            ex.Message);
    }

    [Fact]
    public async Task A_cancelled_load_refuses_the_three_operations()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _load.Status = ShipmentLoadStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(1_000m, [Line(_item, 1_000m)], "T-2"));
        await Assert.ThrowsAsync<DefaultException>(() => UpdateAsync(discharge, 39_500m, HalfAndHalf()));
        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo"));
    }

    /// <summary>
    /// D6: recusa parcial com volta ao armazém deixa a carga "Devolvida". A parte entregue ainda tem
    /// ticket para lançar (o frete depende dele), e lançá-lo ou excluí-lo não tira a carga de Devolvida.
    /// </summary>
    [Fact]
    public async Task A_mixed_returned_load_accepts_and_deletes_a_ticket_and_stays_returned()
    {
        await SeedAsync(status: ShipmentLoadStatus.Returned);

        // Metade da carga voltou ao armazém: a nota dela saiu do jogo e o romaneio de devolução abate
        // o saldo. Os números fecham em "Devolvida" no recálculo.
        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R9",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            RefusedFromShipmentLoadKey = _load.Key,
        });
        await _db.Context.SaveChangesAsync();

        var discharge = await CreateAsync(19_900m, [Line(_item, 19_900m)]);

        Assert.Equal(19_900m, _item.TicketDeliveredQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, _load.Status);

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, _load.Status);
    }

    /// <summary>Review Focus 5: o ticket não "promove" a carga que ainda tem saldo a faturar.</summary>
    [Fact]
    public async Task A_partially_invoiced_load_accepts_a_ticket_without_changing_its_status()
    {
        await SeedAsync(status: ShipmentLoadStatus.PartiallyInvoiced, totalQuantity: 60_000m);

        await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, _load.Status);
        Assert.Empty(LogsOf(ShipmentLoadChangeLogFields.Status));
    }

    /// <summary>Review Focus 3: o peso do ticket é gravado na escala da coluna.</summary>
    [Fact]
    public async Task The_ticket_weight_is_stored_in_three_decimals()
    {
        await SeedAsync();

        var discharge = await CreateAsync(39_500.0004m, HalfAndHalf());

        Assert.Equal(39_500.000m, discharge.DischargedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_weight_is_refused(decimal quantity)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(quantity, HalfAndHalf()));
    }

    [Fact]
    public async Task Blank_ticket_number_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(39_500m, HalfAndHalf(), "   "));
    }
}
