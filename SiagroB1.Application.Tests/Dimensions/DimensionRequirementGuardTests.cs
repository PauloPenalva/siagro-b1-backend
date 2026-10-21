using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseInvoices;
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

    private static async Task<PurchaseInvoice> SeedPurchaseInvoice(
        UnitOfWork db, InvoiceStatus status = InvoiceStatus.Pending, string? ledger = null)
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = status };
        db.Context.PurchaseInvoices.Add(invoice);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", Quantity = 1m, UnitPrice = 1m,
            LedgerAccountCode = ledger,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return invoice;
    }

    private static FinancialDocumentsManualCreateService ManualCreate(UnitOfWork db) =>
        new(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new DimensionCodeValidator(db, Config()), new DimensionDefaultsResolver(db, Config()), Guard(db));

    private static FinancialDocument ManualInput() => new()
    {
        CardCode = "C0001", BranchCode = "01", Direction = FinancialDirection.Receivable,
        DocumentDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), NetAmount = 100m,
    };

    private static async Task<FinancialAdvancesCreateService> AdvanceService(UnitOfWork db)
    {
        db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await db.SaveChangesAsync();
        return new FinancialAdvancesCreateService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new FinancialDocumentsSettleService(db),
            new DimensionCodeValidator(db, Config()), new DimensionDefaultsResolver(db, Config()), Guard(db));
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
        var invoice = await SeedPurchaseInvoice(db);

        var confirm = TaxTestServices.PurchaseConfirm(db, dimensionGuard: Guard(db));
        var ex = await Assert.ThrowsAsync<DefaultException>(() => confirm.ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Informe a conta contábil na linha 1 (MILHO).", ex.Message);
    }

    [Fact]
    public async Task Purchase_confirm_blocked_when_cost_center_required()
    {
        var db = await NewDb(costCenter: true);
        var invoice = await SeedPurchaseInvoice(db, ledger: "1.1.01");

        var confirm = TaxTestServices.PurchaseConfirm(db, dimensionGuard: Guard(db));
        var ex = await Assert.ThrowsAsync<DefaultException>(() => confirm.ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Informe Dimensão 1 na linha 1 (MILHO).", ex.Message);
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
    public async Task Guard_does_not_write_when_settings_row_is_missing()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);

        await Guard(db).EnsureAsync([new DimensionSnapshot()], i => $"{i + 1}");

        Assert.Empty(db.Context.CompanySettings);
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
    public void Draft_services_do_not_take_the_requirement_guard()
    {
        // Nenhum serviço do caminho do rascunho recebe a guarda: ela só existe ao confirmar/criar pronto.
        Type[] draftServices =
        [
            typeof(SalesInvoicesCreateService), typeof(SalesInvoicesUpdateService),
            typeof(SalesInvoicesItemsCreateService), typeof(SalesInvoicesItemsUpdateService),
            typeof(PurchaseInvoicesCreateService), typeof(PurchaseInvoicesUpdateService),
            typeof(PurchaseInvoicesItemsCreateService), typeof(PurchaseInvoicesItemsUpdateService),
        ];

        foreach (var type in draftServices)
        {
            var takesGuard = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(DimensionRequirementGuard));
            Assert.False(takesGuard, $"{type.Name} não deve receber DimensionRequirementGuard.");
        }
    }

    [Fact]
    public async Task Already_confirmed_invoice_is_not_revalidated()
    {
        // As chaves ligam DEPOIS de a nota existir confirmada sem centro nem conta: o estorno passa.
        var db = await NewDb(costCenter: true, ledger: true);
        var invoice = await SeedPurchaseInvoice(db, InvoiceStatus.Confirmed);

        var reverse = new PurchaseInvoicesReverseConfirmService(db,
            FinancialDocumentTestServices.OtherPurchase(db, new FakeDocNumberSequenceService(), "STANDALONE",
                new FakeBusinessPartnerService()));
        await reverse.ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, db.Context.PurchaseInvoices.Single(x => x.Key == invoice.Key).InvoiceStatus);

        // Confirmar de novo uma venda já confirmada cai na regra de pendente, não na obrigatoriedade.
        var sales = await SeedSalesInvoice(db, InvoiceStatus.Confirmed);
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => SalesConfirm(db).ExecuteAsync(sales.Key, "tester"));
        Assert.Equal("Invoice is not pending.", ex.Message);
    }

    [Fact]
    public async Task Positive_path_passes_when_both_fields_filled()
    {
        var db = await NewDb(costCenter: true, ledger: true);

        await Guard(db).EnsureAsync(
            [new DimensionSnapshot { Dim1Code = "CC01", LedgerAccountCode = "1.1.01" }], i => $"{i + 1}");
    }

    [Fact]
    public async Task Second_row_failure_uses_second_row_label()
    {
        var db = await NewDb(costCenter: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).EnsureAsync(
            [new DimensionSnapshot { Dim1Code = "CC01" }, new DimensionSnapshot()], i => $"{i + 1} (X{i})"));

        Assert.Equal("Informe Dimensão 1 na linha 2 (X1).", ex.Message);
    }

    [Fact]
    public async Task Manual_title_create_blocked_when_required()
    {
        var db = await NewDb(costCenter: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ManualCreate(db).ExecuteAsync(ManualInput(), "tester"));

        Assert.Equal("Informe Dimensão 1.", ex.Message);
    }

    [Fact]
    public async Task Manual_title_create_blocked_when_ledger_required()
    {
        var db = await NewDb(ledger: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ManualCreate(db).ExecuteAsync(ManualInput(), "tester"));

        Assert.Equal("Informe a conta contábil.", ex.Message);
    }

    [Fact]
    public async Task Advance_without_contract_blocked_when_required()
    {
        var db = await NewDb(costCenter: true);
        var service = await AdvanceService(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester"));

        Assert.Equal("Informe Dimensão 1.", ex.Message);
        Assert.Empty(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Advance_with_contract_blocked_when_required()
    {
        var db = await NewDb(ledger: true);
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC1", CardCode = "C0001", BranchCode = "01", Status = ContractStatus.Approved,
            ItemCode = "MILHO", UnitOfMeasureCode = "KG", HarvestSeasonCode = "25", DeliveryLocationCode = "L1",
        };
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();
        var service = await AdvanceService(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(
            "Purchase", contract.Key, 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester"));

        Assert.Equal("Informe a conta contábil.", ex.Message);
        Assert.Empty(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Account_entry_blocked_when_required()
    {
        var db = await NewDb(costCenter: true);
        db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await db.SaveChangesAsync();
        var service = new FinancialAccountEntriesCreateService(db, new DimensionCodeValidator(db, Config()), Guard(db));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = DateTime.Today, Amount = 10m, Description = "x",
        }, "tester"));

        Assert.Equal("Informe Dimensão 1.", ex.Message);
        Assert.Empty(db.Context.FinancialAccountEntries);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb(costCenter: true, ledger: true);

        await Guard(db, "SAPB1").EnsureAsync([new DimensionSnapshot()], i => $"{i + 1}");
    }
}
