using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageInvoices;

/// <summary>Spec 2026-10-19 parte 3: a fatura nunca é faturada além do total; rascunho consome saldo.</summary>
public class StorageInvoiceBillingGuardTests
{
    private static SalesInvoice NewDocument(StorageInvoiceBillingSeed seed, decimal amount, string card = "C0001")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = card, BranchCode = "01", InvoiceStatus = InvoiceStatus.Pending,
            StorageInvoiceKey = seed.Invoice.Key,
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SERV", UnitOfMeasureCode = "UN", Quantity = 1m,
            UnitPrice = amount,
        });
        seed.Db.Context.SalesInvoices.Add(invoice);   // como no Create: rastreado e ainda não salvo
        return invoice;
    }

    private static Task EnsureAsync(StorageInvoiceBillingSeed seed, SalesInvoice invoice) =>
        StorageInvoiceBillingGuardService.EnsureCanBillAsync(seed.Db.Context, invoice);

    [Fact]
    public async Task Create_within_balance_is_allowed()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await EnsureAsync(seed, NewDocument(seed, 1000m));
    }

    [Fact]
    public async Task Create_over_balance_is_refused()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var ex = await Assert.ThrowsAsync<DefaultException>(() => EnsureAsync(seed, NewDocument(seed, 1000.01m)));
        Assert.Equal("Valor excede o saldo a faturar da fatura F1 (saldo R$ 1.000,00).", ex.Message);
    }

    [Fact]
    public async Task Draft_consumes_balance()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(600m, InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => EnsureAsync(seed, NewDocument(seed, 500m)));
        Assert.Contains("saldo R$ 400,00", ex.Message);
        Assert.Equal(400m, await StorageInvoiceBillingGuardService.GetBalanceAsync(seed.Db.Context, seed.Invoice.Key));
    }

    [Fact]
    public async Task Cancelled_document_returns_balance()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(600m, InvoiceStatus.Cancelled);

        await EnsureAsync(seed, NewDocument(seed, 1000m));
    }

    [Fact]
    public async Task Item_update_over_balance_is_refused()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(600m, InvoiceStatus.Pending);
        var mine = await seed.AddDocumentAsync(400m, InvoiceStatus.Pending, number: "DS2");
        var line = await seed.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == mine.Key);

        line.UnitPrice = 401m;   // rastreado, ainda não salvo

        await Assert.ThrowsAsync<DefaultException>(() => EnsureAsync(seed, mine));
    }

    [Fact]
    public async Task Other_partner_is_refused()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var ex = await Assert.ThrowsAsync<DefaultException>(() => EnsureAsync(seed, NewDocument(seed, 10m, "C0002")));
        Assert.Equal("O documento de saída precisa ser do mesmo parceiro da fatura F1.", ex.Message);
    }

    [Theory]
    [InlineData(StorageInvoiceStatus.Cancelled)]
    [InlineData(StorageInvoiceStatus.Paid)]
    public async Task Cancelled_or_paid_invoice_is_refused(StorageInvoiceStatus status)
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync(status: status);
        await Assert.ThrowsAsync<DefaultException>(() => EnsureAsync(seed, NewDocument(seed, 10m)));
    }

    [Fact]
    public async Task Document_without_storage_invoice_is_ignored()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        var doc = NewDocument(seed, 99999m);
        doc.StorageInvoiceKey = null;
        await EnsureAsync(seed, doc);
    }

    [Fact]
    public async Task Cancel_storage_invoice_with_active_document_is_refused()
    {
        var seed = await StorageInvoiceBillingSeed.CreateAsync();
        await seed.AddDocumentAsync(400m, InvoiceStatus.Pending);
        var service = new StorageInvoiceCancellationService(seed.Db, new FakeStringLocalizer<Resource>(),
            NullLogger<StorageInvoiceCancellationService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CancelAsync(
            new StorageInvoiceCancelRequest { StorageInvoiceKey = seed.Invoice.Key, Reason = "erro" }, "ana"));
        Assert.Equal("Cancele antes os documentos de saída desta fatura.", ex.Message);
    }
}
