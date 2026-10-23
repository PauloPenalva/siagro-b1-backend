using System.Globalization;
using System.Text;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.SpedFiscal;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.SpedFiscal;

public class SpedFiscalFileServiceTests
{
    private const string Key44 = "41260712345678000195550010000001231000001230";
    private static readonly CultureInfo Pt = new("pt-BR");

    [Fact]
    public async Task Generate_EmptyMonthHasC001IndMov1AndZeroE110()
    {
        var db = await Seeded();

        var lines = await GenerateLines(db, Request());

        Assert.Contains("|C001|1|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|C100|"));
        Assert.Contains("|E110|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|E116|"));
        Assert.Equal("|0000|020|0|01072026|31072026|CEAGUI CEREAIS LTDA|12345678000195||PR|9012345678|4106902|||A|1|", lines[0]);
        Assert.Contains("|D001|1|", lines);
        Assert.Contains("|D990|2|", lines);
        Assert.Contains("|H001|1|", lines);
        Assert.Contains("|K001|1|", lines);
    }

    [Fact]
    public async Task Generate_C190AndE110MatchConference()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.AddRange(OwnSale("V1", 1000m, 120m), OwnSale("V2", 300m, 36m, docNumber: "124"));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", 500m, 60m));
        await Save(db);

        var lines = await GenerateLines(db, Request());
        var conference = await new FiscalConferenceService(db).BuildAsync(new FiscalConferenceRequest { BranchCode = "01", Year = 2026, Month = 7 });

        var c190 = lines.Where(l => l.StartsWith("|C190|")).Select(Fields).ToList();
        Assert.Equal(conference.Summary.Sum(s => s.OperationValue), c190.Sum(f => Dec(f[4])));
        Assert.Equal(conference.Summary.Sum(s => s.IcmsBase), c190.Sum(f => Dec(f[5])));
        Assert.Equal(conference.Summary.Sum(s => s.IcmsValue), c190.Sum(f => Dec(f[6])));
        var e110 = Fields(lines.Single(l => l.StartsWith("|E110|")));
        Assert.Equal(conference.AssessmentTotals.Debits, Dec(e110[1]));
        Assert.Equal(conference.AssessmentTotals.Credits, Dec(e110[5]));
        Assert.Equal(conference.AssessmentTotals.Balance, Dec(e110[12]));
        Assert.Contains("|C190|000|5101|12,00|1000,00|1000,00|120,00|0,00|0,00|0,00|0,00||", lines);
    }

    [Fact]
    public async Task Generate_OwnSaleHasNoC170_ThirdPartyPurchaseHas()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", 500m, 60m));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        var c100 = lines.Where(l => l.StartsWith("|C100|")).ToList();
        Assert.Equal(2, c100.Count);
        // Entrada de terceiro em 16/07 (data de entrada), venda em 15/07: ordem por data
        Assert.Equal("|C100|1|0|C001|55|00|1|123|" + Key44 + "|15072026|15072026|1000,00|2|0,00|0,00|1000,00|0|0,00|0,00|0,00|1000,00|120,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", c100[0]);
        Assert.StartsWith("|C100|0|1|F001|55|00|2|456|41260798765432000110550020000004561000004560|15072026|16072026|500,00|", c100[1]);
        var c170 = Assert.Single(lines, l => l.StartsWith("|C170|"));
        Assert.StartsWith("|C170|1|10001||10,00000|TN|500,00|0,00|0|000|1102||500,00|12,00|60,00|", c170);
        Assert.Equal(lines.IndexOf(c100[1]) + 1, lines.IndexOf(c170));
        Assert.Equal(37, Fields(c170).Length - 1);
        Assert.Equal(28, Fields(c100[0]).Length - 1);
    }

    [Fact]
    public async Task Generate_PreviousCreditLargerThanDebitsCarriesForwardWithoutE116()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m));
        await Save(db);
        var request = Request();
        request.PreviousCreditBalance = 1000m;

        var lines = await GenerateLines(db, request);

        Assert.Contains("|E110|120,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|1000,00|0,00|0,00|0,00|880,00|0,00|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|E116|"));
    }

    [Theory]
    [InlineData(2026, 1, 31, "28022026", "012026")]
    [InlineData(2026, 2, 10, "10032026", "022026")]
    public async Task Generate_PositiveBalanceWritesE116WithDueDate(int year, int month, int dueDay, string dueDate, string reference)
    {
        var db = await Seeded(dueDay);
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m, date: new DateTime(year, month, 5)));
        await Save(db);

        var lines = await GenerateLines(db, Request(year, month));

        Assert.Contains("|E110|120,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|120,00|0,00|120,00|0,00|0,00|", lines);
        Assert.Contains($"|E116|000|120,00|{dueDate}|060501|||||{reference}|", lines);
    }

    [Fact]
    public async Task Generate_CodVer019Before2026()
    {
        var db = await Seeded();

        var lines = await GenerateLines(db, Request(2025, 12));

        Assert.StartsWith("|0000|019|0|01122025|31122025|", lines[0]);
    }

    [Fact]
    public async Task Generate_OwnPurchaseReturnHasIndOper1()
    {
        var db = await Seeded();
        var ret = ThirdPartyPurchase("R1", 500m, 60m);
        ret.IssuerType = DocumentIssuerType.Own;
        ret.InvoiceType = PurchaseInvoiceType.Return;
        ret.NfeStatus = NfeStatus.Authorized;
        ret.Items.Single().Cfop = "5202";
        db.Context.PurchaseInvoices.Add(ret);
        await Save(db);

        var lines = await GenerateLines(db, Request());

        Assert.StartsWith("|C100|1|0|F001|55|00|", Assert.Single(lines, l => l.StartsWith("|C100|")));
        Assert.Contains("|E110|60,00|", lines.Single(l => l.StartsWith("|E110|")));
    }

    [Fact]
    public async Task Generate_VoidedNumberHasNoAccessKey()
    {
        var db = await Seeded();
        var request = Request();
        var data = await Loader(db).LoadAsync(request);
        data.VoidDocuments.Add(new SpedVoidDocument { Direction = "1", Situation = "05", Series = "1", Number = "9", AccessKey = Key44 });
        data.VoidDocuments.Add(new SpedVoidDocument { Direction = "1", Situation = "02", Series = "1", Number = "8", AccessKey = Key44 });

        var lines = Parse(SpedFiscalFileService.Build(data, request));

        Assert.Contains("|C100|1|0||55|05|1|9||" + new string('|', 20), lines);
        Assert.Contains("|C100|1|0||55|02|1|8|" + Key44 + "|" + new string('|', 20), lines);
    }

    [Fact]
    public async Task Generate_InventoryBlockH()
    {
        var db = await Seeded();
        var request = Request(2026, 2);
        var data = await Loader(db).LoadAsync(request);
        request.IncludeInventory = true;
        data.Inventory = new FiscalInventoryData
        {
            Date = new DateTime(2025, 12, 31),
            BranchCode = "01",
            Rows =
            [
                new FiscalInventoryRowDto { ItemCode = "10001", UnitOfMeasureCode = "TN", Quantity = 12.5m, UnitCost = 1500.123456m, Value = 18751.54m },
                new FiscalInventoryRowDto { ItemCode = "10002", UnitOfMeasureCode = "TN", Quantity = 0m, Value = 0m },
            ],
        };
        data.Items.Add(new SpedItem { Code = "10001", Description = "SOJA", UnitCode = "TN", Ncm = "12019000" });

        var lines = Parse(SpedFiscalFileService.Build(data, request));

        Assert.Contains("|H001|0|", lines);
        Assert.Contains("|H005|31122025|18751,54|01|", lines);
        Assert.Contains("|H010|10001|TN|12,500|1500,123456|18751,54|0|||||", lines);
        Assert.Single(lines, l => l.StartsWith("|H010|"));
        Assert.Contains("|H990|4|", lines);
    }

    [Fact]
    public async Task Generate_Block9CountsMatchLines()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", 500m, 60m));
        await Save(db);

        var (content, fileName) = await Service(db).GenerateAsync(Request());
        var lines = Parse(content);

        Assert.Equal("SPED_FISCAL_12345678000195_202607.txt", fileName);
        foreach (var r in lines.Where(l => l.StartsWith("|9900|")).Select(Fields))
            Assert.Equal(lines.Count(l => l.StartsWith($"|{r[1]}|")), int.Parse(r[2]));
        Assert.Equal($"|9999|{lines.Count}|", lines[^1]);
        foreach (var block in new[] { '0', 'B', 'C', 'D', 'E', 'G', 'H', 'K', '1' })
        {
            var closing = Fields(lines.Single(l => l.StartsWith($"|{block}990|")));
            Assert.Equal(lines.Count(l => l[1] == block), int.Parse(closing[1]));
        }
        // ISO-8859-1: "Ã" é um único byte 0xC3
        Assert.Contains((byte)0xC3, content);
        Assert.Contains("|0200|10001|SOJA EM GRÃOS|||TN|00|12019000||||||", lines);
        Assert.Contains("|0150|C001|COOPERATIVA CENTRAL|01058|12345678000195|||4106902||RUA A|10||CENTRO|", lines);
        Assert.Contains("|1010|N|N|N|N|N|N|N|N|N|N|N|N|N|", lines);
    }

    [Fact]
    public async Task Generate_ThrowsWithBlockingMessages()
    {
        var db = await Seeded(settings: false);

        var e = await Assert.ThrowsAsync<BusinessException>(() => Service(db).GenerateAsync(Request()));

        Assert.Contains("Parâmetros do SPED Fiscal não cadastrados para a filial.", e.Message);
    }

    [Fact]
    public async Task Check_ReturnsTotalsAndWarnings()
    {
        var db = await Seeded();
        var sale = OwnSale("V1", 1000m, 100m);
        db.Context.SalesInvoices.Add(sale);
        await Save(db);
        var request = Request();
        request.PreviousCreditBalance = 10m;

        var check = await Service(db).CheckAsync(request);

        Assert.Equal((100m, 0m, 90m), (check.Totals.Debits, check.Totals.Credits, check.Totals.Balance));
        var warning = Assert.Single(check.Issues);
        Assert.False(warning.Blocking);
        Assert.Contains("ICMS difere de base × alíquota", warning.Message);
    }

    // ---- apoio ----

    internal static async Task<IUnitOfWork> Seeded(int dueDay = 10, bool settings = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
            TaxId = "12.345.678/0001-95", StateCode = "PR", StateRegistration = "90.1234567-8",
            MunicipalityCode = "4106902", ZipCode = "80000000", Street = "AV BRASIL", StreetNumber = "100",
            District = "CENTRO", Phone = "(41) 3333-4444",
        });
        if (settings)
            db.Context.SpedFiscalSettings.Add(new SpedFiscalSettings
            {
                BranchCode = "01", Profile = "A", ActivityType = 1, AccountantName = "JOÃO CONTADOR",
                AccountantCpf = "12345678909", AccountantCrc = "PR012345", AccountantMunicipalityCode = "4106902",
                AccountantEmail = "contador@exemplo.com.br", IcmsRevenueCode = "060501", IcmsDueDay = dueDay,
            });
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA EM GRÃOS", Ncm = "1201.90.00" });
        db.Context.UnitsOfMeasure.Add(new UnitOfMeasure { Code = "TN", Description = "TONELADA" });
        db.Context.BusinessPartners.AddRange(
            new BusinessPartner { CardCode = "C001", CardName = "COOPERATIVA CENTRAL", TaxId = "12.345.678/0001-95" },
            new BusinessPartner { CardCode = "F001", CardName = "PRODUTOR RURAL", TaxId = "123.456.789-09" });
        db.Context.Addresses.AddRange(
            new Address { CardCode = "C001", AddressName = "A", AdresType = "B", MunicipalityCode = "4106902", Street = "RUA A", StreetNumber = "10", Block = "CENTRO" },
            new Address { CardCode = "F001", AddressName = "A", AdresType = "B", MunicipalityCode = "4106902", Street = "SITIO", StreetNumber = "SN", Block = "RURAL" });
        await Save(db);
        return db;
    }

    internal static SalesInvoice OwnSale(string number, decimal total, decimal icms, decimal rate = 12m, DateTime? date = null, string docNumber = "123")
    {
        var line = SaleItem(quantity: 10m, unitPrice: total / 10m);
        line.Cfop = "5101";
        line.CstIcms = "00";
        line.IcmsBase = total;
        line.IcmsRate = rate;
        line.IcmsValue = icms;
        var sale = Sale(number, date: date, items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Nfe;
        sale.NfeStatus = NfeStatus.Authorized;
        sale.ChaveNFe = Key44;
        sale.TaxDocumentSeries = "1";
        sale.TaxDocumentNumber = docNumber;
        return sale;
    }

    internal static PurchaseInvoice ThirdPartyPurchase(string number, decimal total, decimal icms)
    {
        var line = PurchaseItem(quantity: 10m, unitPrice: total / 10m);
        line.Cfop = "1102";
        line.CstIcms = "00";
        line.IcmsBase = total;
        line.IcmsRate = 12m;
        line.IcmsValue = icms;
        var purchase = Purchase(number, items: [line]);
        purchase.TaxDocumentKind = TaxDocumentKind.Nfe;
        purchase.TaxDocumentSeries = "2";
        purchase.TaxDocumentNumber = "456";
        purchase.ChaveNFe = "41260798765432000110550020000004561000004560";
        purchase.PostingDate = new DateTime(2026, 7, 16);
        purchase.IssueDate = new DateTime(2026, 7, 15);
        return purchase;
    }

    internal static SpedFiscalDataLoader Loader(IUnitOfWork db) => new(db, new FiscalConferenceService(db),
        new FiscalInventoryService(db, new FiscalKardexService(new FiscalStockLedgerLoader(db)), null!), new SiagroB1.Reports.Services.Sped.SpedMasterDataLoader(db));

    internal static SpedFiscalFileService Service(IUnitOfWork db) => new(Loader(db), new FiscalConferenceService(db));

    internal static SpedFiscalRequest Request(int year = 2026, int month = 7) => new() { BranchCode = "01", Year = year, Month = month };

    private static async Task<List<string>> GenerateLines(IUnitOfWork db, SpedFiscalRequest request) =>
        Parse((await Service(db).GenerateAsync(request)).Content);

    internal static List<string> Parse(byte[] content) =>
        Encoding.Latin1.GetString(content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Campos sem os vazios das pontas: [0] = REG.</summary>
    private static string[] Fields(string line) => line[1..^1].Split('|');

    private static decimal Dec(string s) => decimal.Parse(s, Pt);

    internal static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
