using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ContractStatementReportServiceTests
{
    private static IConfiguration Config(string erp = "STANDALONE") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    private static ContractStatementReportService Service(IUnitOfWork db, IFastReportService? report = null, string erp = "STANDALONE") =>
        new(db, report ?? new RecordingFastReportService(), Config(erp));

    private static StorageTransaction Ticket(string code, StorageTransactionType type, decimal gross, decimal net, DateTime date) => new()
    {
        Key = Guid.NewGuid(), Code = code, TransactionType = type, TransactionDate = date,
        CardCode = "F001", ItemCode = "10001", UnitOfMeasureCode = "KG", WarehouseCode = "ARM01",
        GrossWeight = gross, DryingDiscount = gross - net, NetWeight = net, TruckCode = "ABC1D23",
        InvoiceNumber = "123", InvoiceSerie = "1",
    };

    private static PurchaseInvoice PurchaseNf(string number, PurchaseInvoiceType type, DateTime date,
        InvoiceStatus status = InvoiceStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(), CardCode = "F001", InvoiceType = type, InvoiceStatus = status,
        TaxDocumentNumber = number, TaxDocumentSeries = "1", IssueDate = date,
    };

    private static PurchaseInvoiceItem PurchaseNfItem(PurchaseInvoice nf, PurchaseContract c, decimal qty, decimal price,
        decimal complement = 0m, decimal icms = 0m) => new()
    {
        Key = Guid.NewGuid(), PurchaseInvoiceKey = nf.Key, PurchaseContractKey = c.Key, ItemCode = "10001",
        Quantity = qty, UnitPrice = price, ComplementAmount = complement, IcmsValue = icms, Cfop = "1102",
    };

    private static FinancialDocument Title(string code, FinancialDocumentNature nature, decimal net, decimal settled = 0m,
        FinancialDirection direction = FinancialDirection.Payable, Guid? purchaseKey = null, Guid? salesKey = null,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open, decimal applied = 0m,
        FinancialDocumentOrigin origin = FinancialDocumentOrigin.PurchaseFiscalReconciliation) => new()
    {
        Key = Guid.NewGuid(), Code = code, Nature = nature, NetAmount = net, SettledAmount = settled, AppliedAmount = applied,
        Direction = direction, PurchaseContractKey = purchaseKey, SalesContractKey = salesKey, Status = status,
        CardCode = "F001", DueDate = new DateTime(2026, 9, 30), OriginType = origin,
    };

    [Fact]
    public async Task Purchase_lists_physical_fiscal_and_financial_with_signs()
    {
        var db = TestDb.CreateUnitOfWork();
        var c = Purchase(total: 1_000m, price: 2m);
        var t1 = Ticket("R1", StorageTransactionType.Purchase, 620m, 600m, new DateTime(2026, 8, 1));
        var t2 = Ticket("R2", StorageTransactionType.PurchaseReturn, 50m, 50m, new DateTime(2026, 8, 5));
        db.Context.PurchaseContracts.Add(c);
        db.Context.StorageTransactions.AddRange(t1, t2);
        db.Context.PurchaseContractsAllocations.AddRange(
            new PurchaseContractAllocation { Key = Guid.NewGuid(), PurchaseContractKey = c.Key, StorageTransactionKey = t1.Key, Volume = 600m },
            new PurchaseContractAllocation { Key = Guid.NewGuid(), PurchaseContractKey = c.Key, StorageTransactionKey = t2.Key, Volume = -50m });
        db.Context.PurchaseContractsWashouts.Add(Washout(c, 1, 100m));

        var normal = PurchaseNf("10", PurchaseInvoiceType.Normal, new DateTime(2026, 8, 2));
        var ret = PurchaseNf("11", PurchaseInvoiceType.Return, new DateTime(2026, 8, 6));
        var compl = PurchaseNf("12", PurchaseInvoiceType.ValueComplement, new DateTime(2026, 8, 7));
        var cancelled = PurchaseNf("13", PurchaseInvoiceType.Normal, new DateTime(2026, 8, 8), InvoiceStatus.Cancelled);
        db.Context.PurchaseInvoices.AddRange(normal, ret, compl, cancelled);
        db.Context.PurchaseInvoicesItems.AddRange(
            PurchaseNfItem(normal, c, 600m, 2m, icms: 10m),
            PurchaseNfItem(ret, c, 50m, 2m),
            PurchaseNfItem(compl, c, 0m, 0m, complement: 30m),
            PurchaseNfItem(cancelled, c, 999m, 2m));

        var firm = Title("T1", FinancialDocumentNature.Firm, 1_200m, settled: 500m, purchaseKey: c.Key);
        var back = Title("T2", FinancialDocumentNature.PurchaseReturn, 100m, direction: FinancialDirection.Receivable, purchaseKey: c.Key);
        var advance = Title("T3", FinancialDocumentNature.Advance, 300m, settled: 300m, applied: 200m, purchaseKey: c.Key,
            origin: FinancialDocumentOrigin.Manual);
        var provisional = Title("T4", FinancialDocumentNature.Provisional, 2_000m, purchaseKey: c.Key,
            origin: FinancialDocumentOrigin.PurchaseContractPriceFixation);
        var canceled = Title("T5", FinancialDocumentNature.Firm, 9_999m, purchaseKey: c.Key, status: FinancialDocumentStatus.Canceled);
        db.Context.FinancialDocuments.AddRange(firm, back, advance, provisional, canceled);
        db.Context.FinancialSettlements.AddRange(
            new FinancialSettlement { Key = Guid.NewGuid(), FinancialDocumentKey = firm.Key, Amount = 500m, SettlementDate = new DateTime(2026, 9, 1) },
            new FinancialSettlement { Key = Guid.NewGuid(), FinancialDocumentKey = provisional.Key, Amount = 1m, SettlementDate = new DateTime(2026, 9, 1) });
        await db.Context.SaveChangesAsync();

        var data = await Service(db).PurchaseAsync(c.Key);

        var physical = data.Rows.Where(r => r.Section == ContractStatementReportService.PhysicalSection).ToList();
        Assert.Equal(new[] { ("R1", "Compra", 600m), ("R2", "Devolução", -50m), ("Washout 1", "Washout", 100m) },
            physical.Select(r => (r.Document, r.Movement, r.Q4)).ToArray());
        Assert.Equal((620m, 20m, 600m), (physical[0].Q1, physical[0].Q2, physical[0].Q3));

        var fiscal = data.Rows.Where(r => r.Section == ContractStatementReportService.FiscalSection).ToList();
        Assert.Equal(new[] { ("10/1", 600m, 1_200m), ("11/1", -50m, -100m), ("12/1", 0m, 30m) },
            fiscal.Select(r => (r.Document, r.Q1, r.A1)).ToArray());
        Assert.Equal(10m, fiscal[0].A2);

        var titles = data.Rows.Where(r => r.Section == ContractStatementReportService.TitlesSection).ToList();
        Assert.Equal(new[] { ("T1", 1_200m, 700m), ("T2", -100m, -100m), ("T3", 300m, 0m) },
            titles.Select(r => (r.Document, r.A1, r.A3)).ToArray());

        var settlements = data.Rows.Where(r => r.Section == ContractStatementReportService.SettlementsSection).ToList();
        Assert.Equal(new[] { ("T1", 500m) }, settlements.Select(r => (r.Document, r.A1)).ToArray());

        // Seções em ordem e nenhuma fixação em contrato de preço fixo.
        Assert.Equal(data.Rows.Select(r => r.Section).Order(), data.Rows.Select(r => r.Section));
        Assert.DoesNotContain(data.Rows, r => r.Section == ContractStatementReportService.FixationsSection);

        Assert.Contains("Saldo físico", data.Summary);
        Assert.Contains("350,000 KG", data.Summary);          // 1000 − 550 − 100
        Assert.Contains("1.130,00", data.Summary);            // valor fiscal líquido
        Assert.Contains("Diferença fiscal × financeiro ", data.Summary);
        Assert.Matches(@"Diferença fiscal × financeiro \.*: +30,00", data.Summary); // 1130 − (1200 − 100)
        Assert.Matches(@"Adiantamento não compensado \.*: +100,00", data.Summary);
        Assert.Matches(@"Saldo a pagar \.*: +500,00", data.Summary);               // (700 − 100) − 100
        Assert.Matches(@"Saldo provisório a faturar \.*: +2\.000,00", data.Summary);
        Assert.Contains("Contrato: PC000001", data.Header);
    }

    [Fact]
    public async Task Sales_shows_destination_weight_and_breakage_only_on_closed_owner_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var c = Sales(total: 1_000m, price: 3m, type: ContractType.ToBeDetermined);
        var nf = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C001", InvoiceDate = new DateTime(2026, 8, 10), InvoiceStatus = InvoiceStatus.Confirmed,
            TaxDocumentNumber = "77", TaxDocumentSeries = "1", TruckCode = "XYZ9A99",
        };
        var closedItem = SalesItem(100m, closed: true, delivered: 92m, loss: 2.5m);
        closedItem.SalesInvoiceKey = nf.Key;
        closedItem.SalesContractKey = c.Key;
        closedItem.UnitPrice = 3m;
        db.Context.SalesContracts.Add(c);
        db.Context.SalesInvoices.Add(nf);
        db.Context.SalesInvoicesItems.Add(closedItem);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(c, closedItem, 100m));
        db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(), SalesContractKey = c.Key, FixationVolume = 400m, FixationPrice = 3m,
            Status = PriceFixationStatus.Confirmed, FixationDate = new DateTime(2026, 8, 1),
        });
        db.Context.FinancialDocuments.Add(Title("R1", FinancialDocumentNature.Firm, 300m, direction: FinancialDirection.Receivable,
            salesKey: c.Key, origin: FinancialDocumentOrigin.SalesInvoice));
        await db.Context.SaveChangesAsync();

        var data = await Service(db).SalesAsync(c.Key);

        var physical = Assert.Single(data.Rows, r => r.Section == ContractStatementReportService.PhysicalSection);
        Assert.Equal(("77/1", "Faturamento", "XYZ9A99", 100m, 92m, -10.5m, 100m),
            (physical.Document, physical.Movement, physical.Detail1, physical.Q1, physical.Q2, physical.Q3, physical.Q4));

        var fixation = Assert.Single(data.Rows, r => r.Section == ContractStatementReportService.FixationsSection);
        Assert.Equal(1_200m, fixation.A1);

        Assert.Matches(@"Entregue \.*: +89,500 KG", data.Summary);
        Assert.Matches(@"Valor contratado \.*: +1\.200,00", data.Summary);
        Assert.Matches(@"Diferença fiscal × financeiro \.*: +0,00", data.Summary);
        Assert.Matches(@"Saldo a receber \.*: +300,00", data.Summary);
    }

    [Fact]
    public async Task Renders_a_pdf_with_every_section()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;
        var contentRoot = Path.Combine(Path.GetTempPath(), "siagro-contract-statement-pdf", Guid.NewGuid().ToString("N"));
        try
        {
            var templates = Path.Combine(contentRoot, "Reports", "Templates");
            Directory.CreateDirectory(templates);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", "ContractStatement.frx"),
                Path.Combine(templates, "ContractStatement.frx"));
            var images = Path.Combine(contentRoot, "wwwroot", "images");
            Directory.CreateDirectory(images);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot", "wwwroot", "images", "logo.png"),
                Path.Combine(images, "logo.png"));

            var env = new TestWebHostEnvironment(contentRoot);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp"] = "STANDALONE", ["CompanyName"] = "ACME AGRO LTDA", ["CompanyLogoPath"] = "wwwroot/images/logo.png",
            }).Build();
            var fastReport = new FastReportService(env, configuration,
                new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>(), FakeCompanyContext.Unresolved()),
                FakeCompanyContext.Unresolved());

            var db = TestDb.CreateUnitOfWork();
            var c = Purchase(total: 1_000m, price: 2m, type: ContractType.ToBeDetermined);
            var t1 = Ticket("R1", StorageTransactionType.Purchase, 620m, 600m, new DateTime(2026, 8, 1));
            var nf = PurchaseNf("10", PurchaseInvoiceType.Normal, new DateTime(2026, 8, 2));
            var firm = Title("T1", FinancialDocumentNature.Firm, 1_200m, settled: 500m, purchaseKey: c.Key);
            db.Context.PurchaseContracts.Add(c);
            db.Context.StorageTransactions.Add(t1);
            db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
                { Key = Guid.NewGuid(), PurchaseContractKey = c.Key, StorageTransactionKey = t1.Key, Volume = 600m });
            db.Context.PurchaseInvoices.Add(nf);
            db.Context.PurchaseInvoicesItems.Add(PurchaseNfItem(nf, c, 600m, 2m, icms: 10m));
            db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(), PurchaseContractKey = c.Key, FixationVolume = 600m, FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed, FixationDate = new DateTime(2026, 7, 20),
            });
            db.Context.FinancialDocuments.Add(firm);
            db.Context.FinancialSettlements.Add(new FinancialSettlement
                { Key = Guid.NewGuid(), FinancialDocumentKey = firm.Key, Amount = 500m, SettlementDate = new DateTime(2026, 9, 1) });
            await db.Context.SaveChangesAsync();

            var pdf = await new ContractStatementReportService(db, fastReport, configuration)
                .ExecuteAsync(ContractStatementSide.Purchase, c.Key);

            Assert.True(pdf.Length > 1000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            ReportSample.WriteIfRequested("siagro-contract-statement", "ContractStatement.pdf", pdf);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
                Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_contract_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).PurchaseAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).SalesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Sapb1_blanks_the_icms_column()
    {
        var db = TestDb.CreateUnitOfWork();
        var c = Purchase();
        var nf = PurchaseNf("10", PurchaseInvoiceType.Normal, new DateTime(2026, 8, 2));
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseInvoices.Add(nf);
        db.Context.PurchaseInvoicesItems.Add(PurchaseNfItem(nf, c, 10m, 2m, icms: 5m));
        await db.Context.SaveChangesAsync();
        var recorder = new RecordingFastReportService();

        await Service(db, recorder, "SAPB1").ExecuteAsync(ContractStatementSide.Purchase, c.Key);

        Assert.Equal("ContractStatement.frx", recorder.LastReportName);
        var row = Assert.Single(Assert.IsAssignableFrom<ICollection<ContractStatementRowDto>>(recorder.LastData));
        Assert.Equal((0m, ""), (row.A2, row.HA2));
        Assert.Equal(false, recorder.LastParameters![FastReportService.StandaloneParameter]);
        Assert.Equal("EXTRATO DO CONTRATO DE COMPRA", recorder.LastParameters["pTitle"]);
    }
}
