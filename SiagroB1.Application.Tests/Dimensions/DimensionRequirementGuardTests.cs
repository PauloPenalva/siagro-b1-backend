using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>Obrigatoriedade de centro de custo e conta contábil pelos parâmetros da empresa.</summary>
public class DimensionRequirementGuardTests
{
    private static IConfiguration Config(string erp = "STANDALONE") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    private static async Task<UnitOfWork> NewDb(bool costCenter = false, bool ledger = false)
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        db.Context.CompanySettings.Add(new CompanySettings { Id = 1, RequireCostCenter = costCenter, RequireLedgerAccount = ledger });
        await db.SaveChangesAsync();
        return db;
    }

    private static DimensionRequirementGuard Guard(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, Config(erp), new CompanySettingsService(db));

    private static SalesInvoicesConfirmService SalesConfirm(UnitOfWork db)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(usages),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, "STANDALONE"),
            dimensionGuard: Guard(db));
    }

    private static async Task<SalesInvoice> SeedSalesInvoice(UnitOfWork db, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = status };
        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return invoice;
    }

    [Fact]
    public async Task Blocks_sales_confirm_when_line_missing_cost_center()
    {
        var db = await NewDb(costCenter: true);
        var invoice = await SeedSalesInvoice(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => SalesConfirm(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Informe Dimensão 1 na linha 1 (SOJA).", ex.Message);
    }

    [Fact]
    public async Task Blocks_purchase_confirm_when_line_missing_ledger_account()
    {
        var db = await NewDb(ledger: true);
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending };
        db.Context.PurchaseInvoices.Add(invoice);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", Quantity = 1m, UnitPrice = 1m,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var confirm = TaxTestServices.PurchaseConfirm(db, dimensionGuard: Guard(db));
        var ex = await Assert.ThrowsAsync<DefaultException>(() => confirm.ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Informe a conta contábil na linha 1 (MILHO).", ex.Message);
    }

    [Fact]
    public async Task Both_flags_off_by_default_nothing_blocked()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);

        await Guard(db).EnsureAsync([new DimensionSnapshot()], i => $"{i + 1}");

        var settings = await new CompanySettingsService(db).GetAsync();
        Assert.False(settings.RequireCostCenter);
        Assert.False(settings.RequireLedgerAccount);
    }

    [Fact]
    public async Task Draft_save_is_not_blocked()
    {
        var db = await NewDb(costCenter: true, ledger: true);
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m,
        };
        var create = new SalesInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance,
            dimensionValidator: new DimensionCodeValidator(db, Config()),
            dimensionDefaults: new DimensionDefaultsResolver(db, Config()));

        await create.ExecuteAsync(line, "tester");

        Assert.Null(line.Dim1Code);
    }

    [Fact]
    public async Task Already_confirmed_invoice_is_not_revalidated()
    {
        var db = await NewDb(costCenter: true, ledger: true);
        var invoice = await SeedSalesInvoice(db, InvoiceStatus.Confirmed);

        // Só a regra de "pendente" responde: a obrigatoriedade não roda de novo sobre nota confirmada.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => SalesConfirm(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Invoice is not pending.", ex.Message);
    }

    [Fact]
    public async Task Manual_title_create_blocked_when_required()
    {
        var db = await NewDb(costCenter: true);
        var create = new FinancialDocumentsManualCreateService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new DimensionCodeValidator(db, Config()), new DimensionDefaultsResolver(db, Config()), Guard(db));
        var input = new FinancialDocument
        {
            CardCode = "C0001", BranchCode = "01", Direction = FinancialDirection.Receivable,
            DocumentDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), NetAmount = 100m,
        };

        var ex = await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(input, "tester"));

        Assert.Equal("Informe Dimensão 1.", ex.Message);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb(costCenter: true, ledger: true);

        await Guard(db, "SAPB1").EnsureAsync([new DimensionSnapshot()], i => $"{i + 1}");
    }
}
