using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Terceiro Normal na filial que emite NF-e pelo Siagro: natureza obrigatória (de Entrada) e o motor calcula tudo,
/// como na entrada própria — inclusive no importado do XML (spec terceiro-chave D1, §6).
/// </summary>
public class PurchaseInvoiceThirdPartyTaxationTests
{
    private static PurchaseInvoice ThirdParty(int? usage, string? xml = null)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = SupplierNfeXml.AccessKey,
            TaxDocumentNumber = "456", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 1),
            XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            UsageCode = usage, NfeItemNumber = xml is null ? null : 1,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners(), erp), new FakeDocNumberSequenceService());

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()));

    [Fact]
    public async Task Third_party_normal_line_is_calculated_by_its_nature()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("1102", "51", 18m, 100m, "SP053521", "74"),
            (line.Cfop, line.CstIcms, line.IcmsRate, line.IcmsDeferral, line.IcmsBenefitCode, line.CstPis));
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
    }

    [Fact]
    public async Task Imported_line_gets_the_calculated_taxes_and_keeps_the_supplier_item_number()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var xml = SupplierNfeXml.Build(SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms00(1500m, 12m)));
        var invoice = ThirdParty(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db), xml);

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("51", 18m, "1102", 1), (line.CstIcms, line.IcmsRate, line.Cfop, line.NfeItemNumber!.Value));
    }

    [Fact]
    public async Task Line_without_nature_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(ThirdParty(null), "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Outgoing_nature_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Create(scenario.Db).ExecuteAsync(ThirdParty(scenario.ReturnUsage), "tester"));

        Assert.Equal("A natureza de operação DEVOLUCAO DE COMPRA é de saída e não pode ser usada na entrada.", e.Message);
    }

    [Fact]
    public async Task Customer_return_is_not_calculated()
    {
        // Review Focus 4: "De terceiro + Devolução" é a devolução do cliente e fica como chegou.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(null);
        invoice.InvoiceType = PurchaseInvoiceType.Return;
        invoice.Items.Single().CstIcms = "00";

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("00", (string?)null), (line.CstIcms, line.Cfop));
    }

    [Fact]
    public async Task Branch_without_the_rule_keeps_the_third_party_as_typed()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(null);
        invoice.Items.Single().CstIcms = "00";

        await Create(db, "SAPB1").ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("00", (string?)null), (line.CstIcms, line.Cfop));
    }

    [Fact]
    public async Task Legacy_pending_third_party_without_nature_is_refused_on_save()
    {
        // Review Focus 5: entrada importada antes desta mudança, ainda Pendente e sem natureza nas linhas.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var legacy = ThirdParty(null);
        db.Context.PurchaseInvoices.Add(legacy);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == legacy.Key);
        changed.Comments = "conferido";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Line_added_to_a_third_party_document_is_calculated_without_item_number()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var usage = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db);
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", UnitOfMeasureCode = "KG",
            Quantity = 10m, UnitPrice = 1m, UsageCode = usage, NfeItemNumber = 7,
        };

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(added, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "MILHO" && i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("1102", (int?)null), (line.Cfop, line.NfeItemNumber));
    }

    [Fact]
    public async Task Line_added_to_a_customer_return_keeps_what_was_posted()
    {
        // A devolução do cliente (De terceiro + Devolução) não é calculada, nem na inclusão de linha.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(null);
        invoice.InvoiceType = PurchaseInvoiceType.Return;
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", UnitOfMeasureCode = "KG",
            Quantity = 10m, UnitPrice = 1m, CstIcms = "00", NfeItemNumber = 5,
        };

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(added, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "MILHO" && i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("00", (string?)null, (int?)5), (line.CstIcms, line.Cfop, line.NfeItemNumber));
    }
}
