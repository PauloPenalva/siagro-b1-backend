using System.Globalization;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Impresso da fatura de frete: dados montados (2 CT-e, parcelas, peso, banco) e PDF de ponta a ponta.</summary>
public class FreightInvoiceReportTests : IDisposable
{
    private const string Branch = "01";
    private readonly string _contentRoot;

    public FreightInvoiceReportTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-freight-invoice-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"), "*.frx"))
            File.Copy(file, Path.Combine(templates, Path.GetFileName(file)));

        var images = Path.Combine(_contentRoot, "wwwroot", "images");
        Directory.CreateDirectory(images);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot", "wwwroot", "images", "logo.png"),
            Path.Combine(images, "logo.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static async Task<(IUnitOfWork Db, Guid InvoiceKey)> SeedAsync(bool withBank = true, FreightInvoiceStatus status = FreightInvoiceStatus.Issued)
    {
        var db = TestDb.CreateUnitOfWork();
        var ctx = db.Context;
        ctx.Municipalities.AddRange(
            new Municipality { Code = "5103403", Name = "Cuiabá", StateAbbreviation = "MT" },
            new Municipality { Code = "4106902", Name = "Curitiba", StateAbbreviation = "PR" });
        ctx.Branchs.Add(new Domain.Entities.Branch
        {
            Code = Branch, BranchName = "Matriz", LegalName = "TRANSPORTES ACME LTDA", TaxId = "12345678000199",
            Street = "Av. das Torres", StreetNumber = "1000", District = "Centro", MunicipalityCode = "5103403",
            ZipCode = "78000000", Phone = "6533334444"
        });
        ctx.BusinessPartners.Add(new BusinessPartner { CardCode = "TOM", CardName = "AGRO TOMADORA S/A", TaxId = "98765432000110" });
        ctx.Addresses.Add(new Address
        {
            CardCode = "TOM", AddressName = "Sede", AdresType = "B", Street = "Rua das Flores", StreetNumber = "50",
            City = "Curitiba", State = "PR", ZipCode = "80000000"
        });
        ctx.PaymentConditions.Add(new PaymentCondition { Code = 7, Name = "30 / 60 dias", Days = "30,60", PaymentMeans = "boleto" });
        if (withBank)
            ctx.FinancialAccounts.Add(new FinancialAccount
            {
                Code = "BCO1", Name = "Banco do Brasil", Type = FinancialAccountType.Bank, BankCode = "001",
                BankName = "BANCO DO BRASIL", BankBranch = "1234-5", BankAccountNumber = "98765-0", BranchCode = Branch
            });

        var invoice = new FreightInvoice
        {
            BranchCode = Branch, Code = "FF-000012", TakerCardCode = "TOM", TakerName = "AGRO TOMADORA S/A",
            IssueDate = new DateTime(2026, 10, 22), DueDate = new DateTime(2026, 11, 21), PaymentConditionCode = 7,
            TotalAmount = 1250.50m, Observation = "Pagamento via boleto.", Status = status, CreatedBy = "t"
        };
        ctx.FreightInvoices.Add(invoice);

        var cteA = Cte(1, "01", 30000m);
        var cteB = Cte(2, "02", 12.5m);
        ctx.TransportDocuments.AddRange(cteA, cteB);
        invoice.Items.Add(new FreightInvoiceItem { TransportDocumentKey = cteA.Key, LineNum = 1, Amount = 1000m });
        invoice.Items.Add(new FreightInvoiceItem { TransportDocumentKey = cteB.Key, LineNum = 2, Amount = 250.50m });
        // CT-e desativado (retirado da fatura) não imprime.
        invoice.Items.Add(new FreightInvoiceItem { TransportDocumentKey = Guid.NewGuid(), LineNum = 3, Amount = 99m, IsActive = false });

        foreach (var (n, due, amount) in new[] { (1, new DateTime(2026, 11, 21), 625.25m), (2, new DateTime(2026, 12, 21), 625.25m) })
            ctx.FinancialDocuments.Add(new FinancialDocument
            {
                Code = "FF-000012/" + n, DocNumberKey = Guid.NewGuid(), BranchCode = Branch,
                Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open, CardCode = "TOM", DocumentDate = invoice.IssueDate, DueDate = due,
                NetAmount = amount, InstallmentNumber = n, OriginType = FinancialDocumentOrigin.FreightInvoice,
                OriginKey = invoice.Key, CreatedBy = "t", UpdatedBy = "t"
            });

        await ctx.SaveChangesAsync();
        return (db, invoice.Key);
    }

    private static TransportDocument Cte(int number, string unit, decimal qty) => new()
    {
        BranchCode = Branch, Series = 1, Number = number, AccessKey = $"5126100123456780001995700100000{number:D4}1234567890"[..44],
        Status = CteStatus.Authorized, IssuedAt = new DateTime(2026, 10, 20),
        StartMunicipalityCode = "5103403", StartState = "MT", EndMunicipalityCode = "4106902", EndState = "PR",
        Quantities = [new TransportDocumentQuantity { UnitCode = unit, MeasureType = "PESO BRUTO", Quantity = qty }]
    };

    [Fact]
    public async Task PrintData_TwoCtes_TwoLines_TotalsMatch()
    {
        var (db, key) = await SeedAsync();

        var dto = await new FreightInvoicePrintDataService(db).GetAsync(key);

        Assert.Equal("FF-000012", dto.InvoiceNumber);
        Assert.Equal("22/10/2026", dto.IssueDate);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(dto.TotalAmount, dto.Items.Sum(i => i.Amount));
        Assert.Equal(1250.50m, dto.TotalAmount);
        Assert.Equal(("1", "1", "20/10/2026", "Cuiabá/MT → Curitiba/PR"),
            (dto.Items[0].Number, dto.Items[0].Series, dto.Items[0].IssuedAt, dto.Items[0].Route));
        Assert.Equal(30000m, dto.Items[0].WeightKg);
        Assert.Equal(12500m, dto.Items[1].WeightKg); // TON x 1000
        Assert.Equal(42500m, dto.TotalWeightKg);
    }

    [Fact]
    public async Task PrintData_CarriesEmitterTakerInstallmentsAndBank()
    {
        var (db, key) = await SeedAsync();

        var dto = await new FreightInvoicePrintDataService(db).GetAsync(key);

        Assert.Equal("TRANSPORTES ACME LTDA", dto.EmitterName);
        Assert.Equal("CNPJ 12.345.678/0001-99", dto.EmitterTaxId);
        Assert.Contains("Av. das Torres, 1000", dto.EmitterAddress);
        Assert.Contains("Cuiabá/MT", dto.EmitterAddress);
        Assert.Equal("AGRO TOMADORA S/A", dto.TakerName);
        Assert.Equal("CNPJ 98.765.432/0001-10", dto.TakerTaxId);
        Assert.Contains("Rua das Flores", dto.TakerAddress);
        Assert.Equal("30 / 60 dias", dto.PaymentCondition);
        Assert.Equal(new[] { ("1", "21/11/2026", 625.25m), ("2", "21/12/2026", 625.25m) },
            dto.Installments.Select(i => (i.Installment, i.DueDate, i.Amount)).ToArray());
        Assert.Equal("Banco 001 BANCO DO BRASIL · Agência 1234-5 · Conta 98765-0", dto.BankInfo);
    }

    [Fact]
    public async Task PrintData_NoBankAccount_OmitsBankInfo()
    {
        var (db, key) = await SeedAsync(withBank: false);

        var dto = await new FreightInvoicePrintDataService(db).GetAsync(key);

        Assert.Equal("", dto.BankInfo);
    }

    [Fact]
    public async Task PrintData_CancelledInvoice_IsRefused()
    {
        var (db, key) = await SeedAsync(status: FreightInvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<BusinessException>(() => new FreightInvoicePrintDataService(db).GetAsync(key));
    }

    [Fact]
    public async Task Pdf_IsNonEmpty_AndShowsInstallmentsAndItems()
    {
        var (db, key) = await SeedAsync();
        var env = new TestWebHostEnvironment(_contentRoot);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CompanyName"] = "ACME AGRO LTDA", ["CompanyLogoPath"] = "wwwroot/images/logo.png"
        }).Build();
        var header = new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>(), FakeCompanyContext.Unresolved());
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var pdf = await new FreightInvoiceReportService(env, new FreightInvoicePrintDataService(db), header).GeneratePdfAsync(key);

            Assert.NotEmpty(pdf);
            var folder = Path.Combine(Path.GetTempPath(), "siagro-freight-invoice-reports");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "FreightInvoice.pdf"), pdf);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
