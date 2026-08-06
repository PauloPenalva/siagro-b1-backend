using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Serviços de linha do documento de entrada — o caminho que a grade usa via PATCH em
/// <c>/PurchaseInvoicesItems</c>, e que por isso NÃO passa pelo Update do cabeçalho.
///
/// Daí a guarda de status viver também aqui: sem ela, editar a linha seria a porta dos fundos para
/// alterar documento confirmado.
/// </summary>
public class PurchaseInvoicesItemsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeItemService Catalog() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA DO CADASTRO" });

    private async Task<PurchaseInvoice> SeedAsync(InvoiceStatus status = InvoiceStatus.Pending)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F0001", InvoiceStatus = status,
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Item_name_is_resolved_from_the_catalog_when_absent()
    {
        var invoice = await SeedAsync();

        var item = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 10m,
        };

        await new PurchaseInvoicesItemsCreateService(_db, Catalog()).ExecuteAsync(item, "tester");

        Assert.Equal("SOJA DO CADASTRO", item.ItemName);
    }

    [Fact]
    public async Task Item_name_read_from_the_xml_is_not_overwritten()
    {
        var invoice = await SeedAsync();

        var item = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS (DO XML)",
            Quantity = 10m,
        };

        await new PurchaseInvoicesItemsCreateService(_db, Catalog()).ExecuteAsync(item, "tester");

        // Num documento de terceiro vale a descrição QUE CONSTA NA NOTA.
        Assert.Equal("SOJA EM GRAOS (DO XML)", item.ItemName);
    }

    [Fact]
    public async Task Item_code_absent_from_the_catalog_does_not_blank_the_name()
    {
        var invoice = await SeedAsync();

        var item = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key,
            ItemCode = "CODIGO-DO-EMITENTE",
            ItemName = "PRODUTO QUALQUER",
            Quantity = 1m,
        };

        await new PurchaseInvoicesItemsCreateService(_db, Catalog()).ExecuteAsync(item, "tester");

        // O código vem do emitente e pode não existir no cadastro local — isso é normal aqui.
        Assert.Equal("PRODUTO QUALQUER", item.ItemName);
    }

    [Fact]
    public async Task Line_cannot_be_created_on_a_confirmed_document()
    {
        var invoice = await SeedAsync(InvoiceStatus.Confirmed);

        var item = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 1m,
        };

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesItemsCreateService(_db, Catalog()).ExecuteAsync(item, "tester"));
    }

    [Fact]
    public async Task Line_is_updated_and_the_binding_persists()
    {
        var invoice = await SeedAsync();
        var originKey = Guid.NewGuid();

        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key,
            ItemCode = "SOJA", ItemName = "SOJA", Quantity = 10m,
        };
        _db.Context.PurchaseInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        var incoming = new PurchaseInvoiceItem
        {
            ItemCode = "SOJA", ItemName = "SOJA", Quantity = 12m, UnitPrice = 3m,
            SalesInvoiceItemKey = originKey,
        };

        await new PurchaseInvoicesItemsUpdateService(_db, Catalog())
            .ExecuteAsync(item.Key!.Value, incoming, "tester");

        var reloaded = await _db.Context.PurchaseInvoicesItems.AsNoTracking()
            .FirstAsync(x => x.Key == item.Key);

        Assert.Equal(12m, reloaded.Quantity);
        Assert.Equal(originKey, reloaded.SalesInvoiceItemKey);
    }

    [Fact]
    public async Task Line_cannot_be_updated_on_a_confirmed_document()
    {
        var invoice = await SeedAsync(InvoiceStatus.Confirmed);

        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 1m,
        };
        _db.Context.PurchaseInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        // A porta dos fundos: PATCH direto na linha não passa pelo Update do cabeçalho.
        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesItemsUpdateService(_db, Catalog())
                .ExecuteAsync(item.Key!.Value, new PurchaseInvoiceItem { Quantity = 99m }, "tester"));
    }

    [Fact]
    public async Task Line_is_deleted_from_a_pending_document()
    {
        var invoice = await SeedAsync();

        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 1m,
        };
        _db.Context.PurchaseInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        await new PurchaseInvoicesItemsDeleteService(_db).ExecuteAsync(item.Key!.Value);

        Assert.Empty(_db.Context.PurchaseInvoicesItems);
    }

    [Fact]
    public async Task Line_cannot_be_deleted_from_a_confirmed_document()
    {
        var invoice = await SeedAsync(InvoiceStatus.Confirmed);

        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 1m,
        };
        _db.Context.PurchaseInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesItemsDeleteService(_db).ExecuteAsync(item.Key!.Value));
    }

    [Fact]
    public async Task Unknown_line_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => new PurchaseInvoicesItemsDeleteService(_db).ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Query_all_exposes_the_lines_for_odata()
    {
        var invoice = await SeedAsync();
        _db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", Quantity = 1m,
        });
        await _db.Context.SaveChangesAsync();

        Assert.Single(await new PurchaseInvoicesItemsGetService(_db).QueryAll().ToListAsync());
    }
}
