using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Confirmação de devolução com o destino dos romaneios informado pelo chamador.
/// </summary>
/// <remarks>
/// Antes desta feature <c>ProcessReturnInvoiceAsync</c> percorria TODOS os romaneios da origem e
/// gravava <c>Returned</c> em cada um. Isso está certo para a devolução total, e devolveria
/// romaneios que ninguém recusou numa devolução PARCIAL.
/// <para>
/// E nenhum dos dois destinos novos pode deixar o romaneio em <c>Returned</c>: aquele status o faz
/// sair das consultas de saldo, re-creditando o armazém de origem — errado nos dois, porque em
/// ambos o grão saiu de lá.
/// </para>
/// </remarks>
public class SalesInvoicesReturnShipmentOutcomesTests
{
    private const string CardCode = "C0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

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

    /// <summary>Nota legada confirmada, com dois romaneios faturados de 20.000 cada.</summary>
    private async Task<(SalesInvoice Invoice, StorageTransaction R1, StorageTransaction R2)> SeedAsync()
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            BranchCode = "01",
            InvoiceNumber = "000001",
            TaxDocumentNumber = "12345",
            TaxDocumentSeries = "1",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40_000m,
            UnitPrice = 90m,
        });

        var r1 = Shipment(invoice, "R1");
        var r2 = Shipment(invoice, "R2");

        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.StorageTransactions.AddRange(r1, r2);
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
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
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

    /// <summary>Cria a devolução PENDENTE da nota, na quantidade informada.</summary>
    private async Task<SalesInvoice> PendingReturnAsync(SalesInvoice origin, decimal quantity)
    {
        var originItem = origin.Items.Single();

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(
            origin, "tester",
            new Dictionary<Guid, decimal> { [originItem.Key!.Value] = quantity });

        returnInvoice.Key = Guid.NewGuid();
        returnInvoice.InvoiceNumber = "000002";

        foreach (var item in returnInvoice.Items)
            item.Key = Guid.NewGuid();

        _db.Context.SalesInvoices.Add(returnInvoice);
        await _db.SaveChangesAsync();

        return returnInvoice;
    }

    private Task<StorageTransaction> ShipmentAsync(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    /// <summary>
    /// Destino "segue viagem": o romaneio escolhido volta a ficar disponível — <c>Confirmed</c> e
    /// solto de nota. É o que o faz reaparecer no faturamento e na Montagem de Carga.
    /// </summary>
    [Fact]
    public async Task A_rebilling_outcome_frees_the_chosen_shipment()
    {
        var (invoice, r1, _) = await SeedAsync();
        var returnInvoice = await PendingReturnAsync(invoice, 20_000m);

        await ConfirmService().ExecuteAsync(
            returnInvoice.Key, "tester", CommitMode.Auto,
            new Dictionary<Guid, StorageTransactionsStatus>
            {
                [r1.Key] = StorageTransactionsStatus.Confirmed,
            });

        var freed = await ShipmentAsync(r1.Key);

        Assert.Equal(StorageTransactionsStatus.Confirmed, freed.TransactionStatus);
        Assert.Null(freed.SalesInvoiceKey);
        Assert.False(freed.IsInvoiced);
        Assert.Equal(decimal.Zero, freed.InvoiceQty);
        Assert.Equal(returnInvoice.Key, freed.ReturnInvoiceKey);
    }

    /// <summary>
    /// Destino "volta para armazém": o romaneio escolhido fica <c>Invoiced</c>, continuando a
    /// debitar o armazém de origem — o grão saiu de lá e é creditado em OUTRO lugar pelo romaneio
    /// de devolução. Deixá-lo <c>Returned</c> creditaria os dois.
    /// </summary>
    [Fact]
    public async Task A_warehouse_outcome_keeps_the_chosen_shipment_invoiced()
    {
        var (invoice, r1, _) = await SeedAsync();
        var returnInvoice = await PendingReturnAsync(invoice, 20_000m);

        await ConfirmService().ExecuteAsync(
            returnInvoice.Key, "tester", CommitMode.Auto,
            new Dictionary<Guid, StorageTransactionsStatus>
            {
                [r1.Key] = StorageTransactionsStatus.Invoiced,
            });

        var kept = await ShipmentAsync(r1.Key);

        Assert.Equal(StorageTransactionsStatus.Invoiced, kept.TransactionStatus);
        Assert.Equal(invoice.Key, kept.SalesInvoiceKey);
        Assert.Equal(returnInvoice.Key, kept.ReturnInvoiceKey);
    }

    /// <summary>
    /// O romaneio que não foi escolhido não é tocado. Sem isso, devolver meia nota devolveria a
    /// carreta inteira do vizinho.
    /// </summary>
    [Fact]
    public async Task A_shipment_left_out_is_untouched()
    {
        var (invoice, r1, r2) = await SeedAsync();
        var returnInvoice = await PendingReturnAsync(invoice, 20_000m);

        await ConfirmService().ExecuteAsync(
            returnInvoice.Key, "tester", CommitMode.Auto,
            new Dictionary<Guid, StorageTransactionsStatus>
            {
                [r1.Key] = StorageTransactionsStatus.Confirmed,
            });

        var untouched = await ShipmentAsync(r2.Key);

        Assert.Equal(StorageTransactionsStatus.Invoiced, untouched.TransactionStatus);
        Assert.Equal(invoice.Key, untouched.SalesInvoiceKey);
        Assert.Null(untouched.ReturnInvoiceKey);
        Assert.True(untouched.IsInvoiced);
    }

    /// <summary>
    /// Sem destinos informados o comportamento é o de sempre: todos os romaneios da origem viram
    /// <c>Returned</c>. É o que preserva as devoluções antigas, criadas antes desta feature e
    /// ainda pendentes, que serão confirmadas pela tela.
    /// </summary>
    [Fact]
    public async Task Without_outcomes_every_shipment_is_returned_as_before()
    {
        var (invoice, r1, r2) = await SeedAsync();
        var returnInvoice = await PendingReturnAsync(invoice, 40_000m);

        await ConfirmService().ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Returned, (await ShipmentAsync(r1.Key)).TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Returned, (await ShipmentAsync(r2.Key)).TransactionStatus);
    }
}
