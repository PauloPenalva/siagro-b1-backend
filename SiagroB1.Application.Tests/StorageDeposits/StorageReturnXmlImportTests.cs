using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

/// <summary>Importação do XML do retorno: o NFref do armazém aponta a remessa (spec 2026-10-10 §4).</summary>
public class StorageReturnXmlImportTests
{
    private const string WarehouseCnpj = "11222333000181";
    private const string RemittanceKey = "35261011222333000181550010000001231000001230";

    private static PurchaseInvoicesImportXmlService Service(UnitOfWork db) =>
        new(new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [StorageDepositSeed.Warehouse] = "ARMAZEM TERCEIRO" },
            taxIds: new Dictionary<string, string> { [StorageDepositSeed.Warehouse] = WarehouseCnpj }), db);

    private static byte[] ReturnXml(params string[] referencedKeys) =>
        SupplierNfeXml.Bytes(SupplierNfeXml.BuildReturn(WarehouseCnpj, referencedKeys,
            SupplierNfeXml.Det(1, "GRAO-01", "SOJA DO ARMAZEM", 1000m, 2m, SupplierNfeXml.Icms51(2000m))));

    [Fact]
    public async Task Single_remittance_and_single_item_links_and_takes_our_item_code()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(1000m);
        remittance.ChaveNFe = RemittanceKey;
        await db.SaveChangesAsync();

        var draft = await Service(db).ExecuteAsync(ReturnXml(RemittanceKey), "retorno.xml");

        var item = Assert.Single(draft.Items);
        Assert.Equal(remittance.Items.Single().Key, item.SalesInvoiceItemKey);
        Assert.Equal("SOJA", item.ItemCode);
        Assert.Equal("KG", item.UnitOfMeasureCode);
        Assert.Equal("000000123", item.RemittanceNumber);
    }

    [Fact]
    public async Task Unknown_reference_leaves_the_draft_unlinked()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(1000m);
        remittance.ChaveNFe = RemittanceKey;
        await db.SaveChangesAsync();

        var draft = await Service(db).ExecuteAsync(ReturnXml(new string('9', 44)), "retorno.xml");

        var item = Assert.Single(draft.Items);
        Assert.Null(item.SalesInvoiceItemKey);
        Assert.Null(item.RemittanceNumber);
        Assert.Equal("GRAO-01", item.ItemCode);
    }

    [Fact]
    public async Task Remittance_of_another_warehouse_is_not_linked()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(1000m);
        remittance.ChaveNFe = RemittanceKey;
        remittance.CardCode = "ARM002";
        await db.SaveChangesAsync();

        var draft = await Service(db).ExecuteAsync(ReturnXml(RemittanceKey), "retorno.xml");

        var item = Assert.Single(draft.Items);
        Assert.Null(item.SalesInvoiceItemKey);
        Assert.Equal("GRAO-01", item.ItemCode);
    }

    [Fact]
    public async Task Cancelled_remittance_is_not_linked()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(1000m, InvoiceStatus.Cancelled);
        remittance.ChaveNFe = RemittanceKey;
        await db.SaveChangesAsync();

        var draft = await Service(db).ExecuteAsync(ReturnXml(RemittanceKey), "retorno.xml");

        var item = Assert.Single(draft.Items);
        Assert.Null(item.SalesInvoiceItemKey);
        Assert.Equal("GRAO-01", item.ItemCode);
    }
}
