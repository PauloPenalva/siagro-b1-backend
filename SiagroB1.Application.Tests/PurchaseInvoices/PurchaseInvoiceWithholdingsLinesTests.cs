using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-17 §2: os endpoints de linha recalculam (ou validam) as retenções do cabeçalho.</summary>
public class PurchaseInvoiceWithholdingsLinesTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseInvoicesTaxApplyService Apply() => TaxTestServices.PurchaseApply(_db, new FakeBusinessPartnerService());

    private static FakeItemService Catalog() => new(new Dictionary<string, string> { ["SERV"] = "SERVICO" });

    private static PurchaseInvoiceItem Line(Guid invoice, int usage, decimal price) => new()
    {
        Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice, ItemCode = "SERV", ItemName = "SERVICO", UnitOfMeasureCode = "UN",
        Quantity = 1m, UnitPrice = price, UsageCode = usage,
    };

    private async Task<(PurchaseInvoice Invoice, PurchaseInvoiceItem A, PurchaseInvoiceItem B)> SeedAsync(
        decimal priceA, decimal priceB, bool manual = false, decimal iss = 0m)
    {
        var usage = (await new UsageService(_db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Serviço tomado", Direction = UsageDirection.Incoming, CfopIncomingInState = "1933", CfopIncomingOutState = "2933",
            CreatesFinancialDocument = true, WithholdingIssRate = 5m,
        })).Code;
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F0001", InvoiceStatus = InvoiceStatus.Pending,
            WithholdingsManual = manual, WithholdingBase = priceA + priceB, WithheldIss = iss,
        };
        var a = Line(invoice.Key, usage, priceA);
        var b = Line(invoice.Key, usage, priceB);
        _db.Context.PurchaseInvoices.Add(invoice);
        _db.Context.PurchaseInvoicesItems.AddRange(a, b);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
        return (invoice, a, b);
    }

    private async Task<PurchaseInvoice> ReloadAsync(Guid? key) =>
        await _db.Context.PurchaseInvoices.AsNoTracking().FirstAsync(x => x.Key == key);

    [Fact]
    public async Task Line_add_recomputes_a_non_manual_header()
    {
        var (invoice, a, _) = await SeedAsync(1000m, 0m);
        var added = Line(invoice.Key, a.UsageCode!.Value, 1000m);

        await new PurchaseInvoicesItemsCreateService(_db, Catalog(), Apply()).ExecuteAsync(added, "tester");

        var header = await ReloadAsync(invoice.Key);
        Assert.Equal((2000m, 100m), (header.WithholdingBase, header.WithheldIss));
    }

    [Fact]
    public async Task Line_update_recomputes_a_non_manual_header()
    {
        var (invoice, a, _) = await SeedAsync(1000m, 1000m, iss: 100m);
        var body = Line(invoice.Key, a.UsageCode!.Value, 2000m);

        await new PurchaseInvoicesItemsUpdateService(_db, Catalog(), Apply()).ExecuteAsync(a.Key!.Value, body, "tester");

        var header = await ReloadAsync(invoice.Key);
        Assert.Equal((3000m, 150m), (header.WithholdingBase, header.WithheldIss));
    }

    [Fact]
    public async Task Line_delete_recomputes_a_non_manual_header()
    {
        var (invoice, _, b) = await SeedAsync(1000m, 1000m, iss: 100m);

        await new PurchaseInvoicesItemsDeleteService(_db, Apply()).ExecuteAsync(b.Key!.Value);

        var header = await ReloadAsync(invoice.Key);
        Assert.Equal((1000m, 50m), (header.WithholdingBase, header.WithheldIss));
    }

    [Fact]
    public async Task Manual_header_is_preserved_but_still_validated_on_line_delete()
    {
        var (invoice, _, b) = await SeedAsync(40m, 1000m, manual: true, iss: 50m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsDeleteService(_db, Apply()).ExecuteAsync(b.Key!.Value));

        _db.Context.ChangeTracker.Clear();
        Assert.Equal(2, await _db.Context.PurchaseInvoicesItems.CountAsync(x => x.PurchaseInvoiceKey == invoice.Key));
    }

    [Fact]
    public async Task Manual_header_is_preserved_on_line_update_when_within_the_total()
    {
        var (invoice, a, _) = await SeedAsync(1000m, 1000m, manual: true, iss: 30m);
        var body = Line(invoice.Key, a.UsageCode!.Value, 2000m);

        await new PurchaseInvoicesItemsUpdateService(_db, Catalog(), Apply()).ExecuteAsync(a.Key!.Value, body, "tester");

        var header = await ReloadAsync(invoice.Key);
        Assert.Equal(30m, header.WithheldIss);
    }
}
