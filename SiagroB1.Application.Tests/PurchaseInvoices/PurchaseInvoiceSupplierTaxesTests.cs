// SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierTaxesTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Documento de terceiro com o XML guardado: cada linha com nItem recebe a tributação do det do fornecedor, lida no
/// servidor (spec terceiro D4/D5, §7). Linha sem nItem fica sem fotografia; imposto vindo da tela nunca vale.
/// </summary>
public class PurchaseInvoiceSupplierTaxesTests
{
    private static string Xml() => SupplierNfeXml.Build(
        SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()),
        SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

    private static PurchaseInvoice ThirdParty(string? xml, params PurchaseInvoiceItem[] lines)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.ThirdParty,
            InvoiceType = PurchaseInvoiceType.Normal, XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
            ChaveNFe = SupplierNfeXml.AccessKey, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
        };
        foreach (var line in lines)
            invoice.AddItem(line);
        return invoice;
    }

    private static PurchaseInvoiceItem Line(string code, int? nItem) =>
        new() { Key = Guid.NewGuid(), ItemCode = code, UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 1m, NfeItemNumber = nItem };

    [Fact]
    public void Line_with_an_item_number_gets_the_supplier_taxes()
    {
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1), Line("MILHO", 2));

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        var trigo = invoice.Items.Single(i => i.NfeItemNumber == 1);
        Assert.Equal(("5102", "10019900", (byte?)0, "SP053521"), (trigo.Cfop, trigo.Ncm, trigo.GoodsOrigin, trigo.IcmsBenefitCode));
        Assert.Equal(("51", 1500m, 18m, 100m, 270m, 270m), (trigo.CstIcms, trigo.IcmsBase, trigo.IcmsRate, trigo.IcmsDeferral, trigo.IcmsOperationValue, trigo.IcmsDeferredValue));
        Assert.Equal(("200", "200036", 0.9m, 60m, 0.1m, 60m), (trigo.IbsCbsCst, trigo.IbsCbsClassCode, trigo.CbsRate, trigo.CbsRateReduction, trigo.IbsStateRate, trigo.IbsRateReduction));
        var milho = invoice.Items.Single(i => i.NfeItemNumber == 2);
        Assert.Equal(("00", 12m, 60m), (milho.CstIcms, milho.IcmsRate, milho.IcmsValue));
    }

    [Fact]
    public void Line_without_an_item_number_keeps_no_taxes_even_if_the_body_sent_them()
    {
        var line = Line("TRIGO", null);
        line.CstIcms = "00";
        line.IcmsRate = 18m;
        var invoice = ThirdParty(Xml(), line);

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal(((string?)null, 0m, (int?)null), (line.CstIcms, line.IcmsRate, line.NfeItemNumber));
    }

    [Fact]
    public void Item_number_missing_from_the_xml_is_refused()
    {
        var invoice = ThirdParty(Xml(), Line("TRIGO", 9));

        var e = Assert.Throws<DefaultException>(() => PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items));

        Assert.Equal("Item TRIGO: o item 9 não existe na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public void Document_without_xml_keeps_its_item_numbers_and_no_taxes()
    {
        var line = Line("TRIGO", 3);
        line.CstIcms = "51";
        var invoice = ThirdParty(null, line);

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal((3, (string?)null), (line.NfeItemNumber!.Value, line.CstIcms));
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return)]
    public void Own_entry_and_customer_return_are_not_touched(DocumentIssuerType issuer, PurchaseInvoiceType type)
    {
        var line = Line("TRIGO", 1);
        line.CstIcms = "51";
        line.IcmsRate = 18m;
        var invoice = ThirdParty(Xml(), line);
        invoice.IssuerType = issuer;
        invoice.InvoiceType = type;

        PurchaseInvoiceSupplierTaxes.Apply(invoice, invoice.Items);

        Assert.Equal(("51", 18m, 0m), (line.CstIcms, line.IcmsRate, line.IcmsBase));
    }

    [Fact]
    public async Task Create_fills_the_supplier_taxes_of_a_third_party_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1));

        await Create(db).ExecuteAsync(invoice, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("51", 1, 18m), (saved.CstIcms, saved.NfeItemNumber!.Value, saved.IcmsRate));
    }

    [Fact]
    public async Task Update_keeps_the_supplier_item_after_the_product_changes()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRG", 1));
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.Items.Single().ItemCode = "TRIGO";

        await new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("TRIGO", 1, "51"), (saved.ItemCode, saved.NfeItemNumber!.Value, saved.CstIcms));
    }

    [Fact]
    public async Task Line_added_after_the_import_has_no_item_number()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = ThirdParty(Xml(), Line("TRIGO", 1));
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = Line("MILHO", 2);
        added.PurchaseInvoiceKey = invoice.Key;
        added.CstIcms = "00";

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(added, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "MILHO");
        Assert.Equal(((int?)null, (string?)null), (saved.NfeItemNumber, saved.CstIcms));
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));
}
