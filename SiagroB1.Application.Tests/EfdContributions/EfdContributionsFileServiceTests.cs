using System.Globalization;
using System.Text;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.EfdContributions;
using SiagroB1.Reports.Services.Sped;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.EfdContributions;

public class EfdContributionsFileServiceTests
{
    internal const string Cnpj01 = "12345678000195";
    internal const string Cnpj02 = "12345678000276";
    private const string SupplierCnpj = "98765432000110";
    private static readonly CultureInfo Pt = new("pt-BR");

    internal const int SaleUsage = 1;
    internal const int ExemptSaleUsage = 2;
    internal const int PurchaseUsage = 3;

    [Fact]
    public async Task Generate_EmptyMonthIsValid()
    {
        var db = await Seeded();

        var lines = await GenerateLines(db, Request());

        Assert.Equal("|0000|006|0|||01072026|31072026|CEAGUI CEREAIS LTDA|12345678000195|PR|4106902||00|2|", lines[0]);
        Assert.Contains("|0110|2|||9|", lines);
        Assert.Contains("|0140|01|CEAGUI CEREAIS LTDA|12345678000195|PR|9012345678|4106902|||", lines);
        Assert.Contains("|C001|1|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|C010|"));
        Assert.Contains("|M200|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", lines);
        Assert.Contains("|M600|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|M205|") || l.StartsWith("|M210|"));
        foreach (var open in new[] { "A001", "D001", "F001", "I001", "P001", "1001" })
            Assert.Contains($"|{open}|1|", lines);
        Assert.Equal(13, Fields(lines.Single(l => l.StartsWith("|M200|"))).Length);
    }

    [Fact]
    public async Task Generate_CumulativeOnlyHasNoCreditRegisters()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 0.65m, 3m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        Assert.DoesNotContain(lines, l => l.StartsWith("|M100|") || l.StartsWith("|M105|") || l.StartsWith("|M500|"));
        Assert.Contains("|M200|0,00|0,00|0,00|0,00|0,00|0,00|0,00|6,50|0,00|0,00|6,50|6,50|", lines);
        Assert.Contains("|M205|12|810902|6,50|", lines);
        Assert.Contains("|M210|51|1000,00|1000,00|0,00|0,00|1000,00|0,6500|||6,50|0,00|0,00|0,00|0,00|6,50|", lines);
        Assert.Contains("|M605|12|217201|30,00|", lines);
        Assert.Contains("|M610|51|1000,00|1000,00|0,00|0,00|1000,00|3,0000|||30,00|0,00|0,00|0,00|0,00|30,00|", lines);
        Assert.Equal(16, Fields(lines.Single(l => l.StartsWith("|M210|"))).Length);
        Assert.Contains("|1001|1|", lines);
    }

    [Fact]
    public async Task Generate_NonCumulativeCreditCappedAndCarriedTo1100()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 1.65m, 7.6m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 2000m, "50", 1.65m, 7.6m));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        // Contribuição 16,50 / 76,00; crédito 33,00 / 152,00 → desconta só até a contribuição
        Assert.Contains("|0110|1|1|1||", lines);
        Assert.Contains("|M100|101|0|2000,00|1,6500|||33,00|0,00|0,00|0,00|33,00|1|16,50|16,50|", lines);
        Assert.Contains("|M105|01|50|2000,00|0,00|2000,00|2000,00||||", lines);
        Assert.Contains("|M200|16,50|16,50|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|M205|"));
        Assert.Contains("|M210|01|1000,00|1000,00|0,00|0,00|1000,00|1,6500|||16,50|0,00|0,00|0,00|0,00|16,50|", lines);
        Assert.Contains("|M500|101|0|2000,00|7,6000|||152,00|0,00|0,00|0,00|152,00|1|76,00|76,00|", lines);
        Assert.Contains("|M600|76,00|76,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|", lines);
        Assert.Contains("|1001|0|", lines);
        Assert.Contains("|1100|072026|01||101|33,00|0,00|33,00|0,00|0,00|0,00|33,00|16,50|0,00|0,00|0,00|0,00|16,50|", lines);
        Assert.Contains("|1500|072026|01||101|152,00|0,00|152,00|0,00|0,00|0,00|152,00|76,00|0,00|0,00|0,00|0,00|76,00|", lines);
        // Hierarquia: M105 logo abaixo do M100
        Assert.Equal(lines.FindIndex(l => l.StartsWith("|M100|")) + 1, lines.FindIndex(l => l.StartsWith("|M105|")));
    }

    [Fact]
    public async Task Generate_NonCumulativeCreditBelowContributionHasM205()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 1.65m, 7.6m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 400m, "50", 1.65m, 7.6m));
        await Save(db);

        var check = await Service(db).CheckAsync(Request());
        var lines = await GenerateLines(db, Request());

        Assert.Equal((16.50m, 6.60m, 9.90m), (check.Totals.PisDue, check.Totals.PisCredit, check.Totals.PisPayable));
        Assert.Equal((76.00m, 30.40m, 45.60m), (check.Totals.CofinsDue, check.Totals.CofinsCredit, check.Totals.CofinsPayable));
        Assert.Contains("|M100|101|0|400,00|1,6500|||6,60|0,00|0,00|0,00|6,60|0|6,60|0,00|", lines);
        Assert.Contains("|M205|08|691201|9,90|", lines);
        Assert.Contains("|M605|08|585601|45,60|", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|1100|"));
        Assert.Contains("|1001|1|", lines);
    }

    [Fact]
    public async Task Generate_CreditCodeByCstGroup()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 10000m, "01", 1.65m, 7.6m));
        var p2 = ThirdPartyPurchase("P2", "01", 300m, "51", 1.65m, 7.6m);
        p2.TaxDocumentNumber = "457";
        p2.ChaveNFe = NfeKey(SupplierCnpj, "2", "457");
        db.Context.PurchaseInvoices.AddRange(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m), p2);
        await Save(db);

        var lines = await GenerateLines(db, Request());

        var m100 = lines.Where(l => l.StartsWith("|M100|")).ToList();
        Assert.Equal(new[]
        {
            "|M100|101|0|500,00|1,6500|||8,25|0,00|0,00|0,00|8,25|0|8,25|0,00|",
            "|M100|201|0|300,00|1,6500|||4,95|0,00|0,00|0,00|4,95|0|4,95|0,00|",
        }, m100);
        Assert.Equal("|M105|01|50|500,00|0,00|500,00|500,00||||", lines[lines.IndexOf(m100[0]) + 1]);
        Assert.Equal("|M105|01|51|300,00|0,00|300,00|300,00||||", lines[lines.IndexOf(m100[1]) + 1]);
        Assert.Equal(2, lines.Count(l => l.StartsWith("|M500|")));
    }

    [Fact]
    public async Task Generate_C100UsesEachDocumentsOwnKey()
    {
        var db = await Seeded(withBranch02: true);
        db.Context.SalesInvoices.AddRange(Sale("V1", "01", 1000m, "01", 0.65m, 3m), Sale("V2", "02", 500m, "01", 0.65m, 3m, docNumber: "124"));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        var keys = lines.Where(l => l.StartsWith("|C100|")).Select(l => Fields(l)[8]).ToList();
        Assert.Equal(new[] { NfeKey(Cnpj01, "1", "123"), NfeKey(Cnpj02, "1", "124") }, keys);
        Assert.Equal("41260712345678000276550010000001241000001240", keys[1]);
        Assert.All(lines.Where(l => l.StartsWith("|C100|")), l => Assert.Equal("9", Fields(l)[12]));
    }

    [Fact]
    public async Task Generate_SubstitutionRevenueCst05GoesToM400()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(Sale("V1", "01", 400m, "05", 0m, 0m, usage: ExemptSaleUsage));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        Assert.Contains("|M400|05|400,00|||", lines);
        Assert.Contains("|M800|05|400,00|||", lines);
    }

    [Fact]
    public async Task Generate_RevenueWithSuspensionGoesToM410ByNature()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.AddRange(
            Sale("V1", "01", 1000m, "09", 0m, 0m, usage: ExemptSaleUsage),
            Sale("V2", "01", 300m, "09", 0m, 0m, usage: ExemptSaleUsage, docNumber: "124"),
            Sale("V3", "01", 200m, "06", 0m, 0m, usage: ExemptSaleUsage, docNumber: "125"));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        Assert.Contains("|M400|06|200,00|||", lines);
        Assert.Contains("|M400|09|1300,00|||", lines);
        Assert.Contains("|M410|105|1300,00|||", lines);
        Assert.Contains("|M800|09|1300,00|||", lines);
        Assert.Equal(lines.IndexOf("|M400|09|1300,00|||") + 1, lines.IndexOf("|M410|105|1300,00|||"));
        Assert.Equal(2, lines.Count(l => l.StartsWith("|M410|")));
    }

    [Fact]
    public async Task Generate_TwoEstablishmentsTwoC010()
    {
        var db = await Seeded(withBranch02: true);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 0.65m, 3m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "02", 500m, "70", 0m, 0m));
        await Save(db);

        var lines = await GenerateLines(db, Request("02"));

        Assert.Equal(2, lines.Count(l => l.StartsWith("|0140|")));
        var c010 = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("|C010|")).ToList();
        Assert.Equal(new[] { "|C010|12345678000195|2|", "|C010|12345678000276|2|" }, c010.Select(x => x.l));
        // Venda (C001) sob o C010 da matriz; entrada (F001) sob o da filial 02
        Assert.StartsWith("|C100|1|0|C001|55|00|1|123|", lines[c010[0].i + 1]);
        Assert.StartsWith("|C100|0|1|F001|55|00|2|456|", lines[c010[1].i + 1]);
        // 0150 do F001 só sob o 0140 da filial 02
        var second0140 = lines.FindLastIndex(l => l.StartsWith("|0140|"));
        Assert.Equal(second0140, lines.FindLastIndex(l => l.StartsWith("|0140|02|")));
        Assert.True(lines.FindIndex(l => l.StartsWith("|0150|F001|")) > second0140);
        Assert.True(lines.FindIndex(l => l.StartsWith("|0150|C001|")) < second0140);
        Assert.Equal("|0000|006|0|||01072026|31072026|CEAGUI CEREAIS LTDA|12345678000195|PR|4106902||00|2|", lines[0]);
    }

    [Fact]
    public async Task Generate_C100AndC170CarryLineTaxes()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 0.65m, 3m));
        await Save(db);

        var lines = await GenerateLines(db, Request());

        var c100 = Assert.Single(lines, l => l.StartsWith("|C100|"));
        Assert.Equal("|C100|1|0|C001|55|00|1|123|" + NfeKey(Cnpj01, "1", "123") + "|15072026|15072026|1000,00|9|0,00|0,00|1000,00|0|0,00|0,00|0,00|0,00|0,00|0,00|0,00|0,00|6,50|30,00|0,00|0,00|", c100);
        var c170 = Assert.Single(lines, l => l.StartsWith("|C170|"));
        Assert.Equal("|C170|1|10001||10,00000|TN|1000,00|0,00|0|041|5101||0,00|0,00|0,00|0,00|0,00|0,00||||0,00|0,00|0,00|01|1000,00|0,6500|||6,50|01|1000,00|3,0000|||30,00||",
            c170);
        Assert.Equal(37, Fields(c170).Length);
        Assert.Equal(29, Fields(c100).Length);
        Assert.Equal(12, Fields(lines.Single(l => l.StartsWith("|0200|"))).Length);
    }

    [Fact]
    public async Task Generate_VoidOwnNfeUnderItsC010()
    {
        var db = await Seeded();
        var cancelled = Sale("V9", "01", 100m, "01", 0.65m, 3m, docNumber: "200");
        cancelled.NfeStatus = NfeStatus.Cancelled;
        cancelled.InvoiceStatus = InvoiceStatus.Cancelled;
        db.Context.SalesInvoices.Add(cancelled);
        await Save(db);

        var lines = await GenerateLines(db, Request());

        Assert.Contains("|C001|0|", lines);
        Assert.Contains("|C010|12345678000195|2|", lines);
        Assert.Contains("|C100|1|0||55|02|1|200|" + NfeKey(Cnpj01, "1", "200") + "|" + new string('|', 20), lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("|C170|"));
    }

    [Theory]
    [InlineData(PisCofinsRegime.NonCumulative, PisCofinsRegime.NonCumulative, "|0110|1|1|1||")]
    [InlineData(PisCofinsRegime.Cumulative, PisCofinsRegime.Cumulative, "|0110|2|||9|")]
    [InlineData(PisCofinsRegime.NonCumulative, PisCofinsRegime.Cumulative, "|0110|3|1|1||")]
    public async Task Generate_IncTribFromBranchRegimes(PisCofinsRegime regime01, PisCofinsRegime regime02, string expected)
    {
        var db = await Seeded(regime01: regime01, withBranch02: true, regime02: regime02);

        var lines = await GenerateLines(db, Request());

        Assert.Contains(expected, lines);
    }

    [Fact]
    public async Task Generate_MixedRegimesAggregatePerCodCont()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative, withBranch02: true);
        db.Context.SalesInvoices.AddRange(
            Sale("V1", "01", 1000m, "01", 1.65m, 7.6m),
            Sale("V2", "02", 500m, "01", 0.65m, 3m, docNumber: "124"),
            Sale("V3", "02", 300m, "01", 0.65m, 3m, docNumber: "125"));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "02", 800m, "50", 1.65m, 7.6m));
        await Save(db);

        var lines = await GenerateLines(db, Request("01"));

        Assert.Contains("|M210|01|1000,00|1000,00|0,00|0,00|1000,00|1,6500|||16,50|0,00|0,00|0,00|0,00|16,50|", lines);
        Assert.Contains("|M210|51|800,00|800,00|0,00|0,00|800,00|0,6500|||5,20|0,00|0,00|0,00|0,00|5,20|", lines);
        // Entrada da filial cumulativa não gera crédito
        Assert.DoesNotContain(lines, l => l.StartsWith("|M100|"));
        Assert.Contains("|M200|16,50|0,00|0,00|16,50|0,00|0,00|16,50|5,20|0,00|0,00|5,20|21,70|", lines);
        Assert.Contains("|M205|08|691201|16,50|", lines);
        Assert.Contains("|M205|12|810902|5,20|", lines);
    }

    [Fact]
    public async Task Generate_Block9CountsMatchLines()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative, withBranch02: true);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 1.65m, 7.6m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 2000m, "50", 1.65m, 7.6m));
        await Save(db);

        var (content, fileName) = await Service(db).GenerateAsync(Request());
        var lines = Parse(content);

        Assert.Equal("EFD_CONTRIBUICOES_12345678000195_202607.txt", fileName);
        foreach (var r in lines.Where(l => l.StartsWith("|9900|")).Select(Fields))
            Assert.Equal(lines.Count(l => l.StartsWith($"|{r[1]}|")), int.Parse(r[2]));
        Assert.Equal($"|9999|{lines.Count}|", lines[^1]);
        foreach (var block in new[] { '0', 'A', 'C', 'D', 'F', 'I', 'M', 'P', '1' })
        {
            var closing = Fields(lines.Single(l => l.StartsWith($"|{block}990|")));
            Assert.Equal(lines.Count(l => l[1] == block), int.Parse(closing[1]));
        }
        // Ordem dos blocos do leiaute
        var order = lines.Select(l => l[1]).Distinct().ToArray();
        Assert.Equal(new[] { '0', 'A', 'C', 'D', 'F', 'I', 'M', 'P', '1', '9' }, order);
        // ISO-8859-1: "Ã" é um único byte 0xC3
        Assert.Contains((byte)0xC3, content);
    }

    [Fact]
    public async Task Generate_RectifyingCarriesPreviousReceipt()
    {
        var db = await Seeded();
        var request = Request();
        request.Purpose = 1;
        request.PreviousReceiptNumber = "AB.CD.12.34.56.78.90";

        var lines = await GenerateLines(db, request);

        Assert.StartsWith("|0000|006|1||AB.CD.12.34.56.78.90|01072026|", lines[0]);
    }

    [Fact]
    public async Task Generate_ThrowsWithBlockingMessages()
    {
        var db = await Seeded(settings: false);

        var e = await Assert.ThrowsAsync<BusinessException>(() => Service(db).GenerateAsync(Request()));

        Assert.Contains("Parâmetros do SPED Fiscal não cadastrados para a filial.", e.Message);
    }

    [Fact]
    public void LayoutVersion_ByPeriod()
    {
        Assert.Equal("005", EfdContributionsFileService.LayoutVersion(new DateTime(2019, 12, 1)));
        Assert.Equal("006", EfdContributionsFileService.LayoutVersion(new DateTime(2020, 1, 1)));
        Assert.Equal("006", EfdContributionsFileService.LayoutVersion(new DateTime(2026, 7, 1)));
    }

    // ---- apoio ----

    internal static async Task<IUnitOfWork> Seeded(bool settings = true, PisCofinsRegime regime01 = PisCofinsRegime.Cumulative,
        bool withBranch02 = false, PisCofinsRegime regime02 = PisCofinsRegime.Cumulative, bool headOffice = true)
    {
        var db = TestDb.CreateUnitOfWork();
        if (headOffice)
            db.Context.Branchs.Add(new Branch
            {
                Code = "01", BranchName = "MATRIZ", LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
                TaxId = "12.345.678/0001-95", StateCode = "PR", StateRegistration = "90.1234567-8",
                MunicipalityCode = "4106902", PisCofinsRegime = regime01,
            });
        if (withBranch02)
            db.Context.Branchs.Add(new Branch
            {
                Code = "02", BranchName = "FILIAL", LegalName = "CEAGUI CEREAIS LTDA", TaxId = "12.345.678/0002-76",
                StateCode = "PR", StateRegistration = "90.7654321-0", MunicipalityCode = "4113700", PisCofinsRegime = regime02,
            });
        if (settings)
            db.Context.SpedFiscalSettings.Add(new SpedFiscalSettings
            {
                BranchCode = "01", Profile = "A", ActivityType = 1, AccountantName = "JOÃO CONTADOR",
                AccountantCpf = "12345678909", AccountantCrc = "PR012345", AccountantMunicipalityCode = "4106902",
                AccountantEmail = "contador@exemplo.com.br", IcmsRevenueCode = "060501", IcmsDueDay = 10,
            });
        db.Context.Usages.AddRange(
            new Usage { Code = SaleUsage, Name = "VENDA" },
            new Usage { Code = ExemptSaleUsage, Name = "VENDA SUSPENSA", PisCofinsRevenueNatureCode = "105" },
            new Usage { Code = PurchaseUsage, Name = "COMPRA", PisCofinsCreditBaseCode = "01" });
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

    /// <summary>NF-e própria de venda com PIS/COFINS gravados (valor = base × alíquota).</summary>
    internal static SalesInvoice Sale(string number, string branch, decimal total, string cst, decimal pisRate, decimal cofinsRate,
        int usage = SaleUsage, string docNumber = "123")
    {
        var line = SaleItem(quantity: 10m, unitPrice: total / 10m);
        line.Cfop = "5101";
        line.CstIcms = "41";
        line.UsageCode = usage;
        SetPisCofins(line, total, cst, pisRate, cofinsRate);
        var sale = InvoiceReportSeed.Sale(number, branchCode: branch, items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Nfe;
        sale.NfeStatus = NfeStatus.Authorized;
        sale.ChaveNFe = NfeKey(branch == "02" ? Cnpj02 : Cnpj01, "1", docNumber);
        sale.TaxDocumentSeries = "1";
        sale.TaxDocumentNumber = docNumber;
        return sale;
    }

    internal static PurchaseInvoice ThirdPartyPurchase(string number, string branch, decimal total, string cst, decimal pisRate,
        decimal cofinsRate, int usage = PurchaseUsage)
    {
        var line = PurchaseItem(quantity: 10m, unitPrice: total / 10m);
        line.Cfop = "1102";
        line.CstIcms = "41";
        line.UsageCode = usage;
        line.CstPis = cst;
        line.PisBase = total;
        line.PisRate = pisRate;
        line.PisValue = Math.Round(total * pisRate / 100m, 2);
        line.CstCofins = cst;
        line.CofinsBase = total;
        line.CofinsRate = cofinsRate;
        line.CofinsValue = Math.Round(total * cofinsRate / 100m, 2);
        var purchase = Purchase(number, branchCode: branch, items: [line]);
        purchase.TaxDocumentKind = TaxDocumentKind.Nfe;
        purchase.TaxDocumentSeries = "2";
        purchase.TaxDocumentNumber = "456";
        purchase.ChaveNFe = NfeKey(SupplierCnpj, "2", "456");
        return purchase;
    }

    /// <summary>Chave de acesso de 44 dígitos (PR, 07/2026, modelo 55) com o DV módulo 11 da SEFAZ.</summary>
    internal static string NfeKey(string cnpj, string series, string number)
    {
        var body = $"412607{cnpj}55{int.Parse(series):000}{long.Parse(number):000000000}1{long.Parse(number):00000000}";
        int sum = 0, weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        var dv = 11 - sum % 11;
        return body + (dv >= 10 ? 0 : dv);
    }

    private static void SetPisCofins(SalesInvoiceItem line, decimal total, string cst, decimal pisRate, decimal cofinsRate)
    {
        line.CstPis = cst;
        line.PisBase = pisRate == 0 ? 0m : total;
        line.PisRate = pisRate;
        line.PisValue = Math.Round(total * pisRate / 100m, 2);
        line.CstCofins = cst;
        line.CofinsBase = cofinsRate == 0 ? 0m : total;
        line.CofinsRate = cofinsRate;
        line.CofinsValue = Math.Round(total * cofinsRate / 100m, 2);
    }

    internal static EfdContributionsDataLoader Loader(IUnitOfWork db) =>
        new(db, new FiscalConferenceService(db), new SpedMasterDataLoader(db));

    internal static EfdContributionsFileService Service(IUnitOfWork db) => new(Loader(db), new FiscalConferenceService(db));

    internal static EfdContributionsRequest Request(string branch = "01") => new() { BranchCode = branch, Year = 2026, Month = 7 };

    internal static async Task<List<string>> GenerateLines(IUnitOfWork db, EfdContributionsRequest request) =>
        Parse((await Service(db).GenerateAsync(request)).Content);

    internal static List<string> Parse(byte[] content) =>
        Encoding.Latin1.GetString(content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Campos sem os vazios das pontas: [0] = REG.</summary>
    internal static string[] Fields(string line) => line[1..^1].Split('|');

    internal static decimal Dec(string s) => decimal.Parse(s, Pt);

    internal static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
