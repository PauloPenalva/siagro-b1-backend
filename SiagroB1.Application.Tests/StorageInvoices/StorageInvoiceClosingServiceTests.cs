using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageInvoices;

/// <summary>
/// Fechamento da fatura de serviços de armazém. O período digitado não é mais uma trava:
/// uma fatura ativa sobreposta não impede o fechamento — a nova fatura leva só os registros
/// ainda pendentes (<c>IsInvoiced = false</c>) do período. Caso real: MHAgro, lote L00000023,
/// F00000009 até 21/04 e usuário fechando 20/04–07/09 com a armazenagem de 22/04 em diante
/// nunca faturada.
/// </summary>
public class StorageInvoiceClosingServiceTests
{
    private const string Lot = "L00000023";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageInvoiceClosingService Service() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            new FakeStringLocalizer<Resource>(),
            NullLogger<StorageInvoiceClosingService>.Instance);

    private static StorageInvoiceCloseRequest Request() => new()
    {
        DocNumberKey = Guid.NewGuid(),
        StorageAddressCode = Lot,
        ClosingDate = new DateTime(2026, 9, 15),
        PeriodStart = new DateTime(2026, 4, 20),
        PeriodEnd = new DateTime(2026, 9, 7),
    };

    /// <summary>Lote com a fatura F00000009 (23/03–21/04) já fechada e sua cobrança de 21/04.</summary>
    private async Task<(StorageInvoice Invoice, StorageCharge Charge)> SeedInvoicedPeriodAsync()
    {
        _db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = Lot,
            Description = "Lote de teste",
            CardCode = "C0001",
            CardName = "Cliente",
            ItemCode = "SOJA",
            WarehouseCode = "01",
            UoM = "KG",
        });

        var invoice = new StorageInvoice
        {
            Key = Guid.NewGuid(),
            Code = "F00000009",
            StorageAddressCode = Lot,
            CardCode = "C0001",
            PeriodStart = new DateTime(2026, 3, 23),
            PeriodEnd = new DateTime(2026, 4, 21),
            Status = StorageInvoiceStatus.Closed,
            TotalAmount = 4m,
        };
        _db.Context.StorageInvoices.Add(invoice);

        var charge = new StorageCharge
        {
            Key = Guid.NewGuid(),
            StorageAddressCode = Lot,
            ChargeType = StorageChargeType.Storage,
            PeriodStart = new DateTime(2026, 4, 21),
            PeriodEnd = new DateTime(2026, 4, 21),
            TotalAmount = 4m,
            IsInvoiced = true,
            StorageInvoiceKey = invoice.Key,
        };
        _db.Context.StorageCharges.Add(charge);

        await _db.Context.SaveChangesAsync();
        return (invoice, charge);
    }

    [Fact]
    public async Task Overlapping_active_invoice_does_not_block_and_new_invoice_takes_only_pending_records()
    {
        var (oldInvoice, invoicedCharge) = await SeedInvoicedPeriodAsync();

        var pendingCharge = new StorageCharge
        {
            Key = Guid.NewGuid(),
            StorageAddressCode = Lot,
            ChargeType = StorageChargeType.Storage,
            PeriodStart = new DateTime(2026, 4, 22),
            PeriodEnd = new DateTime(2026, 4, 22),
            TonDays = 244886.829m,
            UnitPriceOrRate = 0.00022223m,
            TotalAmount = 54.42m,
        };
        var pendingShipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "00002832",
            StorageAddressCode = Lot,
            TransactionDate = new DateTime(2026, 6, 18),
            TransactionType = StorageTransactionType.Shipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            NetWeight = 1000m,
            ShipmentPrice = 5m,
        };
        _db.Context.StorageCharges.Add(pendingCharge);
        _db.Context.StorageTransactions.Add(pendingShipment);
        await _db.Context.SaveChangesAsync();

        var created = await Service().CloseAsync(Request(), "tester");

        Assert.NotEqual(oldInvoice.Key, created.Key);
        Assert.Equal(2, created.Items.Count);
        Assert.Equal(59.42m, created.TotalAmount);
        Assert.DoesNotContain(created.Items, x => x.SourceKey == invoicedCharge.Key);

        var charges = await _db.Context.StorageCharges.AsNoTracking().ToListAsync();
        Assert.Equal(oldInvoice.Key, charges.Single(x => x.Key == invoicedCharge.Key).StorageInvoiceKey);
        Assert.Equal(created.Key, charges.Single(x => x.Key == pendingCharge.Key).StorageInvoiceKey);

        var shipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == pendingShipment.Key);
        Assert.True(shipment.IsInvoiced);
        Assert.Equal(created.Key, shipment.StorageInvoiceKey);

        Assert.Equal(2, await _db.Context.StorageInvoices.CountAsync(x => x.Status != StorageInvoiceStatus.Cancelled));
    }

    [Fact]
    public async Task Overlapping_period_without_pending_records_reports_nothing_to_invoice()
    {
        await SeedInvoicedPeriodAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service().CloseAsync(Request(), "tester"));

        Assert.Equal("NO_ITEMS_TO_INVOICE", ex.Message);
        Assert.Equal(1, await _db.Context.StorageInvoices.CountAsync());
    }
}
