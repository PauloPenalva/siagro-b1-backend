using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using Fixture = SiagroB1.Application.Tests.Financials.FinancialDocumentsSalesNoContractTests.Fixture;

namespace SiagroB1.Application.Tests.StorageInvoices;

/// <summary>Spec 2026-10-19 parte 3: a situação da fatura acompanha confirmar/cancelar/estornar o documento e baixar o título.</summary>
public class StorageInvoiceStatusTriggersTests
{
    private static async Task<(Fixture F, StorageInvoice Storage)> CreateAsync(decimal total = 1000m)
    {
        var f = await Fixture.CreateAsync();
        var storage = new StorageInvoice
        {
            Key = Guid.NewGuid(), Code = "F1", StorageAddressCode = "LOTE1", CardCode = "C0001",
            PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), TotalAmount = total,
        };
        f.Seed.Context.StorageInvoices.Add(storage);
        await f.Seed.Context.SaveChangesAsync();
        return (f, storage);
    }

    private static Task<SalesInvoice> LinkedAsync(Fixture f, StorageInvoice storage, decimal amount) =>
        f.NoContractInvoiceAsync(amount, tweak: i => i.StorageInvoiceKey = storage.Key);

    private static async Task<StorageInvoiceStatus> StatusAsync(Fixture f, StorageInvoice storage) =>
        (await f.Seed.Context.StorageInvoices.AsNoTracking().SingleAsync(i => i.Key == storage.Key)).Status;

    private static async Task SettleAllAsync(Fixture f, SalesInvoice invoice)
    {
        foreach (var t in await f.TitlesAsync(invoice))
            await new FinancialDocumentsSettleService(f.Seed.Db)
                .ExecuteAsync(t.Key, "CX01", t.NetAmount, DateTime.Today, 0m, 0m, 0m, null, null, "ana");
    }

    [Fact]
    public async Task Confirm_linked_document_sets_partially_invoiced()
    {
        var (f, storage) = await CreateAsync();
        var doc = await LinkedAsync(f, storage, 400m);

        await f.ConfirmAsync(doc);

        Assert.Equal(StorageInvoiceStatus.PartiallyInvoiced, await StatusAsync(f, storage));
    }

    [Fact]
    public async Task Settle_last_receivable_sets_paid()
    {
        var (f, storage) = await CreateAsync();
        var doc = await LinkedAsync(f, storage, 1000m);
        await f.ConfirmAsync(doc);
        Assert.Equal(StorageInvoiceStatus.Invoiced, await StatusAsync(f, storage));

        await SettleAllAsync(f, doc);

        Assert.Equal(StorageInvoiceStatus.Paid, await StatusAsync(f, storage));
    }

    [Fact]
    public async Task Reverse_settlement_returns_to_invoiced()
    {
        var (f, storage) = await CreateAsync();
        var doc = await LinkedAsync(f, storage, 1000m);
        await f.ConfirmAsync(doc);
        var title = Assert.Single(await f.TitlesAsync(doc));
        var settlement = await new FinancialDocumentsSettleService(f.Seed.Db)
            .ExecuteAsync(title.Key, "CX01", 1000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        await new FinancialDocumentsReverseSettlementService(f.Seed.Db).ExecuteAsync(settlement.Key, "estorno", "ana");

        Assert.Equal(StorageInvoiceStatus.Invoiced, await StatusAsync(f, storage));
    }

    [Fact]
    public async Task Cancel_linked_document_reopens_invoice()
    {
        var (f, storage) = await CreateAsync();
        var a = await LinkedAsync(f, storage, 400m);
        var b = await LinkedAsync(f, storage, 600m);
        await f.ConfirmAsync(a);
        await f.ConfirmAsync(b);

        await f.Seed.Cancel(f.Service()).ExecuteAsync(b.Key, "ana");
        Assert.Equal(StorageInvoiceStatus.PartiallyInvoiced, await StatusAsync(f, storage));

        await f.Seed.ReverseConfirm(f.Service()).ExecuteAsync(a.Key, "ana");
        Assert.Equal(StorageInvoiceStatus.Open, await StatusAsync(f, storage));
    }

    [Fact]
    public async Task Settling_unrelated_title_does_not_touch_storage_invoice()
    {
        var (f, storage) = await CreateAsync();
        var other = await f.NoContractInvoiceAsync(500m);
        await f.ConfirmAsync(other);

        await SettleAllAsync(f, other);

        Assert.Equal(StorageInvoiceStatus.Open, await StatusAsync(f, storage));
    }
}
