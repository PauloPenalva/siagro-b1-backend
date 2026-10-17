using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Vínculo do CT-e com as NF-e transportadas (spec 2026-10-17): informativo, resolvido pela chave de acesso.</summary>
public class PurchaseInvoiceTransportedDocumentsTests
{
    private const string SaleKeyText = "35261000000000000000550010000000011000000017";
    private const string EntryKeyText = "35261000000000000000550010000000022000000024";
    private const string UnknownKeyText = "35261000000000000000550010000000033000000031";

    private static string CteKey()
    {
        var first43 = "3526" + "10" + "00052998224725" + "57" + "001" + "000000456" + "1" + "12345678";
        return first43 + SupplierNfeKeyGuard.CheckDigit(first43);
    }

    private static byte[] Xml(params string[] keys) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><cteProc xmlns=\"http://www.portalfiscal.inf.br/cte\" versao=\"4.00\">" +
        "<CTe><infCte versao=\"4.00\"><ide><mod>57</mod><serie>1</serie><nCT>456</nCT></ide><infCTeNorm><infDoc>" +
        string.Concat(keys.Select(k => $"<infNFe><chave>{k}</chave></infNFe>")) +
        "</infDoc></infCTeNorm></infCte></CTe></cteProc>");

    private static PurchaseInvoice Cte(int usage, byte[]? xml)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Cte, ChaveNFe = CteKey(), TaxDocumentNumber = "456", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 1), FreightStartState = "SP", FreightEndState = "PR",
            FreightPayerTaxId = "12345678000195", XmlData = xml,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 500m, UsageCode = usage,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()), new FakeDocNumberSequenceService());

    private sealed record Scenario(UnitOfWork Db, Guid SaleKey, Guid EntryKey, int Usage);

    /// <summary>Uma saída com a chave de venda e uma entrada com a chave de entrada.</summary>
    private static async Task<Scenario> SeedAsync()
    {
        var s = await PurchaseNfeTestSeed.SeedAsync();
        var sale = await s.Db.Context.SalesInvoices.SingleAsync(x => x.Key == s.SaleKey);
        sale.ChaveNFe = SaleKeyText;
        var entry = await s.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == s.InvoiceKey);
        entry.ChaveNFe = EntryKeyText;
        await s.Db.SaveChangesAsync();
        return new Scenario(s.Db, s.SaleKey, s.InvoiceKey, await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(s.Db));
    }

    private static async Task<PurchaseInvoice> CreateCteAsync(Scenario s, params string[] keys)
    {
        var cte = Cte(s.Usage, Xml(keys));
        await Create(s.Db).ExecuteAsync(cte, "tester");
        return cte;
    }

    private static PurchaseInvoicesTransportedDocumentsService Service(UnitOfWork db) => new(db);

    private static Task<List<PurchaseInvoiceTransportedDocument>> LinksAsync(UnitOfWork db, Guid cteKey) =>
        db.Context.PurchaseInvoiceTransportedDocuments.AsNoTracking().Where(x => x.PurchaseInvoiceKey == cteKey)
            .OrderBy(x => x.AccessKey).ToListAsync();

    [Fact]
    public async Task Create_seeds_links_from_cte_xml_matching_sales_and_purchase_invoices()
    {
        var s = await SeedAsync();

        var cte = await CreateCteAsync(s, SaleKeyText, EntryKeyText, SaleKeyText);

        var links = await LinksAsync(s.Db, cte.Key);
        Assert.Equal(2, links.Count);
        var sale = links.Single(x => x.AccessKey == SaleKeyText);
        Assert.Equal(s.SaleKey, sale.SalesInvoiceKey);
        Assert.Null(sale.TransportedPurchaseInvoiceKey);
        var entry = links.Single(x => x.AccessKey == EntryKeyText);
        Assert.Equal(s.EntryKey, entry.TransportedPurchaseInvoiceKey);
        Assert.Null(entry.SalesInvoiceKey);
    }

    [Fact]
    public async Task Create_without_xml_seeds_nothing()
    {
        var s = await SeedAsync();
        var cte = Cte(s.Usage, null);

        await Create(s.Db).ExecuteAsync(cte, "tester");

        Assert.Empty(await LinksAsync(s.Db, cte.Key));
    }

    [Fact]
    public async Task Unknown_key_is_kept_without_link()
    {
        var s = await SeedAsync();

        var cte = await CreateCteAsync(s, UnknownKeyText);

        var link = Assert.Single(await LinksAsync(s.Db, cte.Key));
        Assert.Equal(UnknownKeyText, link.AccessKey);
        Assert.Null(link.SalesInvoiceKey);
        Assert.Null(link.TransportedPurchaseInvoiceKey);
    }

    [Fact]
    public async Task Cancelled_invoices_are_not_linked()
    {
        var s = await SeedAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(x => x.Key == s.SaleKey)).InvoiceStatus = InvoiceStatus.Cancelled;
        (await s.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == s.EntryKey)).InvoiceStatus = InvoiceStatus.Cancelled;
        await s.Db.SaveChangesAsync();

        var cte = await CreateCteAsync(s, SaleKeyText, EntryKeyText);

        Assert.All(await LinksAsync(s.Db, cte.Key), l =>
        {
            Assert.Null(l.SalesInvoiceKey);
            Assert.Null(l.TransportedPurchaseInvoiceKey);
        });
    }

    [Fact]
    public async Task Relink_finds_invoice_created_later()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, UnknownKeyText);
        (await s.Db.Context.SalesInvoices.SingleAsync(x => x.Key == s.SaleKey)).ChaveNFe = UnknownKeyText;
        await s.Db.SaveChangesAsync();

        await Service(s.Db).RelinkAsync(cte.Key);

        var link = Assert.Single(await LinksAsync(s.Db, cte.Key));
        Assert.Equal(s.SaleKey, link.SalesInvoiceKey);
    }

    [Fact]
    public async Task Add_resolves_and_remove_deletes()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s);

        var added = await Service(s.Db).AddAsync(cte.Key, SaleKeyText);
        Assert.Equal(s.SaleKey, added.SalesInvoiceKey);

        await Service(s.Db).RemoveAsync(added.Key);
        Assert.Empty(await LinksAsync(s.Db, cte.Key));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("3526100000000000000055001000000001100000001X")]
    [InlineData(null)]
    public async Task Add_rejects_malformed_key(string? key)
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(s.Db).AddAsync(cte.Key, key));

        Assert.Equal("A chave de acesso deve ter 44 dígitos.", e.Message);
    }

    [Fact]
    public async Task Add_rejects_duplicate_key()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, SaleKeyText);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(s.Db).AddAsync(cte.Key, SaleKeyText));

        Assert.Equal("Esta chave já está vinculada ao CT-e.", e.Message);
    }

    [Fact]
    public async Task Links_cannot_change_after_confirmation()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, SaleKeyText);
        var link = (await LinksAsync(s.Db, cte.Key)).Single();
        (await s.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == cte.Key)).InvoiceStatus = InvoiceStatus.Confirmed;
        await s.Db.SaveChangesAsync();
        const string message = "Só é possível alterar os vínculos de um CT-e pendente.";

        Assert.Equal(message, (await Assert.ThrowsAsync<DefaultException>(() => Service(s.Db).AddAsync(cte.Key, UnknownKeyText))).Message);
        Assert.Equal(message, (await Assert.ThrowsAsync<DefaultException>(() => Service(s.Db).RemoveAsync(link.Key))).Message);
        Assert.Equal(message, (await Assert.ThrowsAsync<DefaultException>(() => Service(s.Db).RelinkAsync(cte.Key))).Message);
    }

    [Fact]
    public async Task Deleting_referenced_sales_invoice_clears_link()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, SaleKeyText);
        (await s.Db.Context.SalesInvoices.SingleAsync(x => x.Key == s.SaleKey)).InvoiceStatus = InvoiceStatus.Pending;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();
        var service = new SalesInvoicesDeleteService(
            s.Db, new ShipmentLoadsBalanceHookService(s.Db.Context, new ShipmentLoadsMovementLogService(s.Db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

        await service.ExecuteAsync(s.SaleKey, "tester");

        Assert.False(await s.Db.Context.SalesInvoices.AnyAsync(x => x.Key == s.SaleKey));
        var link = Assert.Single(await LinksAsync(s.Db, cte.Key));
        Assert.Null(link.SalesInvoiceKey);
        Assert.Equal(SaleKeyText, link.AccessKey);
    }

    [Fact]
    public async Task Deleting_referenced_purchase_invoice_clears_link()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, EntryKeyText);
        s.Db.Context.ChangeTracker.Clear();

        await new PurchaseInvoicesDeleteService(s.Db).ExecuteAsync(s.EntryKey);

        Assert.False(await s.Db.Context.PurchaseInvoices.AnyAsync(x => x.Key == s.EntryKey));
        var link = Assert.Single(await LinksAsync(s.Db, cte.Key));
        Assert.Null(link.TransportedPurchaseInvoiceKey);
    }

    [Fact]
    public async Task Deleting_cte_removes_its_links()
    {
        var s = await SeedAsync();
        var cte = await CreateCteAsync(s, SaleKeyText, UnknownKeyText);
        s.Db.Context.ChangeTracker.Clear();

        await new PurchaseInvoicesDeleteService(s.Db).ExecuteAsync(cte.Key);

        Assert.False(await s.Db.Context.PurchaseInvoices.AnyAsync(x => x.Key == cte.Key));
        Assert.Empty(await LinksAsync(s.Db, cte.Key));
    }
}
