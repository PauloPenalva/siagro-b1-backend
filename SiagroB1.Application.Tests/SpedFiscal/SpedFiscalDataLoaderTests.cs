using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.SpedFiscal;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.SpedFiscal;

public class SpedFiscalDataLoaderTests
{
    private const string Key44 = "35260712345678000195550010000001231000001230";

    [Fact]
    public async Task Load_OwnCancelledNfeBecomesVoidDocumentWithSituation02()
    {
        var db = await Seeded();
        var cancelled = OwnSale("CANC", NfeStatus.Cancelled, InvoiceStatus.Cancelled);
        cancelled.TaxDocumentSeries = "1";
        cancelled.TaxDocumentNumber = "123";
        db.Context.SalesInvoices.AddRange(cancelled, OwnSale("OK"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        var v = Assert.Single(data.VoidDocuments);
        Assert.Equal(("1", "02", "55", "1", "123", (string?)Key44), (v.Direction, v.Situation, v.Model, v.Series, v.Number, v.AccessKey));
        Assert.Equal(new[] { "OK" }, data.Lines.Select(l => l.InternalNumber));
    }

    [Fact]
    public async Task Load_RuralProducerIndividualUsesCpf()
    {
        var db = await Seeded();
        db.Context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = "F001", CardName = "PRODUTOR", TaxId = "12.345.678/0001-95",
            IsRuralProducer = true, RuralProducerType = RuralProducerType.Individual, RuralProducerCpf = "123.456.789-01",
        });
        db.Context.Addresses.AddRange(
            new Address { CardCode = "F001", AddressName = "A", AdresType = "S" },
            new Address { CardCode = "F001", AddressName = "B", AdresType = "B", MunicipalityCode = "4106902", Street = "RUA X", StreetNumber = "10", Block = "CENTRO" });
        db.Context.PurchaseInvoices.Add(Purchase("P1", items: [Line(PurchaseItem())]));
        await Save(db);

        var p = Assert.Single((await Loader(db).LoadAsync(Request())).Participants);

        Assert.Equal(("", "12345678901", "4106902", "RUA X", "10", "CENTRO"), (p.Cnpj, p.Cpf, p.MunicipalityCode, p.Street, p.Number, p.District));
        Assert.Equal("01058", p.CountryCode);
    }

    [Fact]
    public async Task Load_ParticipantsAndItemsAreDistinct()
    {
        var db = await Seeded();
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C001", CardName = "COOP", TaxId = "12.345.678/0001-95" });
        db.Context.Items.Add(new Item { ItemCode = "FRETE", ItemName = "FRETE" });
        db.Context.SalesInvoices.AddRange(OwnSale("V1"), OwnSale("V2"), OwnSale("V3", item: "FRETE"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        var p = Assert.Single(data.Participants);
        Assert.Equal(("C001", "12345678000195", ""), (p.Code, p.Cnpj, p.Cpf));
        Assert.Equal(new[] { "10001", "FRETE" }, data.Items.Select(i => i.Code));
        var soy = data.Items[0];
        Assert.Equal(("SOJA", "TN", "00", "12019000"), (soy.Description, soy.UnitCode, soy.ItemType, soy.Ncm));
        Assert.Equal("09", data.Items[1].ItemType);
        Assert.Equal(new[] { "TN" }, data.Units.Select(u => u.Code));
        Assert.Equal("TONELADA", data.Units[0].Description);
    }

    [Fact]
    public async Task Load_ConversionWhenLineUnitDiffersFromItemUnit()
    {
        var db = await Seeded();
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "10001", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        db.Context.UnitsOfMeasure.AddRange(new UnitOfMeasure { Code = "KG", Description = "QUILO" }, new UnitOfMeasure { Code = "SC", Description = "SACA" });
        db.Context.PurchaseInvoices.AddRange(Third("P1", "KG"), Third("P2", "KG"), Third("P3", "SC"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        Assert.Equal("KG", data.Items.Single().UnitCode);
        var c = Assert.Single(data.Conversions);
        Assert.Equal(("10001", "SC", 60m), (c.ItemCode, c.UnitCode, c.Factor));
        Assert.Equal(new[] { "KG", "SC" }, data.Units.Select(u => u.Code));
    }

    [Fact]
    public async Task Load_ThirdPartyCancelledIsIgnored()
    {
        var db = await Seeded();
        var third = Purchase("T1", items: [Line(PurchaseItem())]);
        third.TaxDocumentKind = TaxDocumentKind.Nfe;
        third.NfeStatus = NfeStatus.Cancelled;
        third.ChaveNFe = Key44;
        var own = Purchase("O1", issuer: DocumentIssuerType.Own, items: [Line(PurchaseItem())]);
        own.TaxDocumentKind = TaxDocumentKind.Nfe;
        own.NfeStatus = NfeStatus.Voided;
        db.Context.PurchaseInvoices.AddRange(third, own);
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        var v = Assert.Single(data.VoidDocuments);
        Assert.Equal(("0", "05"), (v.Direction, v.Situation));
        Assert.Empty(data.Lines);
    }

    [Fact]
    public async Task Load_PartnerWithoutRecordIsReportedMissing()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        Assert.Empty(data.Participants);
        Assert.Equal(new[] { "C001" }, data.MissingParticipantCodes);
    }

    [Fact]
    public async Task Load_LineUnitWithoutFactorIsReportedUnconvertible()
    {
        var db = await Seeded();
        db.Context.PurchaseInvoices.AddRange(Third("P1", "TN"), Third("P2", "TN"), Third("P3", "SC"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        Assert.Empty(data.Conversions);
        Assert.Equal(new[] { new SpedUnconvertibleUnit("10001", "SC") }, data.UnconvertibleUnits);
    }

    [Fact]
    public async Task Load_OwnNfeUnitWithoutFactorDoesNotBlock()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.AddRange(OwnSale("V1"), OwnSale("V2"), OwnSale("V3", uom: "SC"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request());

        Assert.Equal("TN", data.Items.Single().UnitCode);
        Assert.Empty(data.Conversions);
        Assert.Empty(data.UnconvertibleUnits);
    }

    [Fact]
    public async Task LoadItems_InventoryUnitDifferentFromItemUnitYieldsConversion()
    {
        var db = await Seeded();
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "10001", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        db.Context.PurchaseInvoices.AddRange(Third("P1", "SC"), Third("P2", "SC"));
        await Save(db);
        var lines = (await Loader(db).LoadAsync(Request())).Lines;

        var (items, conversions, unconvertible) = await Loader(db).LoadItemsAsync(new Branch { Code = "01", BranchName = "MATRIZ" }, lines, Inventory("KG"));

        Assert.Equal("SC", items.Single().UnitCode);
        var c = Assert.Single(conversions);
        Assert.Equal(("10001", "KG"), (c.ItemCode, c.UnitCode));
        Assert.Empty(unconvertible);
    }

    [Fact]
    public async Task LoadItems_InventoryUnitWithoutFactorIsUnconvertible()
    {
        var db = await Seeded();
        db.Context.PurchaseInvoices.AddRange(Third("P1", "TN"), Third("P2", "TN"));
        await Save(db);
        var lines = (await Loader(db).LoadAsync(Request())).Lines;

        var (_, conversions, unconvertible) = await Loader(db).LoadItemsAsync(new Branch { Code = "01", BranchName = "MATRIZ" }, lines, Inventory("SC"));

        Assert.Empty(conversions);
        Assert.Equal(new[] { new SpedUnconvertibleUnit("10001", "SC") }, unconvertible);
    }

    [Theory]
    [InlineData(SalesInvoiceType.Normal, "5101", "1")]
    [InlineData(SalesInvoiceType.Return, "1202", "0")]
    [InlineData(SalesInvoiceType.Return, "", "0")]
    public async Task Load_VoidSaleDirectionFollowsOperation(SalesInvoiceType type, string cfop, string expected)
    {
        var db = await Seeded();
        var sale = OwnSale("CANC", NfeStatus.Cancelled, InvoiceStatus.Cancelled);
        sale.InvoiceType = type;
        sale.Items.Single().Cfop = cfop;
        db.Context.SalesInvoices.Add(sale);
        await Save(db);

        Assert.Equal(expected, Assert.Single((await Loader(db).LoadAsync(Request())).VoidDocuments).Direction);
    }

    [Theory]
    [InlineData(PurchaseInvoiceType.Normal, "1101", "0")]
    [InlineData(PurchaseInvoiceType.Return, "5202", "1")]
    [InlineData(PurchaseInvoiceType.Return, "", "1")]
    public async Task Load_VoidOwnPurchaseDirectionFollowsOperation(PurchaseInvoiceType type, string cfop, string expected)
    {
        var db = await Seeded();
        var item = PurchaseItem();
        item.Cfop = cfop;
        var own = Purchase("O1", type: type, issuer: DocumentIssuerType.Own, items: [item]);
        own.TaxDocumentKind = TaxDocumentKind.Nfe;
        own.NfeStatus = NfeStatus.Cancelled;
        db.Context.PurchaseInvoices.Add(own);
        await Save(db);

        Assert.Equal(expected, Assert.Single((await Loader(db).LoadAsync(Request())).VoidDocuments).Direction);
    }

    private static PurchaseInvoice Third(string number, string uom) => Purchase(number, items: [Line(PurchaseItem(uom: uom))]);

    private static FiscalInventoryData Inventory(string uom) => new()
    {
        Rows = [new FiscalInventoryRowDto { ItemCode = "10001", UnitOfMeasureCode = uom, Quantity = 5m, Value = 10m }],
    };

    private static PurchaseInvoiceItem Line(PurchaseInvoiceItem item)
    {
        item.Cfop = "1101";
        return item;
    }

    private static SalesInvoice OwnSale(string number, NfeStatus nfe = NfeStatus.Authorized,
        InvoiceStatus status = InvoiceStatus.Confirmed, string item = "10001", string uom = "TN")
    {
        var line = SaleItem(itemCode: item, uom: uom);
        line.Cfop = "5101";
        var sale = Sale(number, status: status, items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Nfe;
        sale.NfeStatus = nfe;
        sale.ChaveNFe = Key44;
        return sale;
    }

    private static async Task<IUnitOfWork> Seeded()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", FreightServiceItemCode = "FRETE" });
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA", Ncm = "12019000" });
        db.Context.UnitsOfMeasure.Add(new UnitOfMeasure { Code = "TN", Description = "TONELADA" });
        await Save(db);
        return db;
    }

    private static SpedFiscalDataLoader Loader(IUnitOfWork db) => new(db, new FiscalConferenceService(db),
        new FiscalInventoryService(db, new FiscalKardexService(new FiscalStockLedgerLoader(db)), null!));

    private static SpedFiscalRequest Request() => new() { BranchCode = "01", Year = 2026, Month = 7 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
