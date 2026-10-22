using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Accounting;
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
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>
/// Onde a guarda da conta roda: confirmação pedida pelo usuário, título manual, adiantamento e lançamento na conta.
/// Fluxos do sistema (confirmação sem a flag: faturamento, devolução, recusa, NF-e autorizada) e rascunho não travam.
/// </summary>
public class AccountingAssignmentPlacementTests
{
    private const string Ledger = "4.1";

    private static async Task<UnitOfWork> NewDb(bool requiresCostCenter = true)
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = Ledger, Name = "Despesa", RequiresCostCenter = requiresCostCenter });
        await db.SaveChangesAsync();
        return db;
    }

    private static AccountingAssignmentGuard Guard(UnitOfWork db) => new(db, Config());

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

    private static async Task<SalesInvoice> SeedSalesInvoice(UnitOfWork db)
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m, LedgerAccountCode = Ledger,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return invoice;
    }

    private static async Task<PurchaseInvoice> SeedPurchaseInvoice(UnitOfWork db)
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending };
        db.Context.PurchaseInvoices.Add(invoice);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", Quantity = 1m, UnitPrice = 1m,
            LedgerAccountCode = Ledger,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return invoice;
    }

    [Fact]
    public async Task User_sales_confirm_blocks_line_missing_cost_center()
    {
        var db = await NewDb();
        var invoice = await SeedSalesInvoice(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesConfirm(db).ExecuteAsync(invoice.Key, "tester", enforceAccountingAssignment: true));

        Assert.Equal($"A conta {Ledger} exige centro de custo (linha 1 (SOJA)).", ex.Message);
    }

    [Fact]
    public async Task System_sales_confirm_is_not_blocked()
    {
        var db = await NewDb();
        var invoice = await SeedSalesInvoice(db);

        // Sem a flag: é como o faturamento de romaneio, a devolução e a NF-e autorizada chamam.
        var ex = await Record.ExceptionAsync(() => SalesConfirm(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.False(ex is DefaultException d && d.Message.Contains("exige centro de custo"), ex?.Message);
    }

    [Fact]
    public async Task User_purchase_confirm_blocks_and_system_confirm_does_not()
    {
        var db = await NewDb();
        var invoice = await SeedPurchaseInvoice(db);

        var confirm = TaxTestServices.PurchaseConfirm(db, dimensionGuard: Guard(db));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            confirm.ExecuteAsync(invoice.Key, "tester", enforceAccountingAssignment: true));
        Assert.Contains("exige centro de custo", ex.Message);

        var system = await Record.ExceptionAsync(() =>
            TaxTestServices.PurchaseConfirm(db, dimensionGuard: Guard(db)).ExecuteAsync(invoice.Key, "tester"));
        Assert.False(system is DefaultException d && d.Message.Contains("exige centro de custo"), system?.Message);
    }

    [Fact]
    public void Draft_services_do_not_take_the_guard()
    {
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
                .Any(p => p.ParameterType == typeof(AccountingAssignmentGuard));
            Assert.False(takesGuard, $"{type.Name} não deve receber AccountingAssignmentGuard.");
        }
    }

    [Fact]
    public async Task Manual_title_create_blocked_by_account()
    {
        var db = await NewDb();
        var service = new FinancialDocumentsManualCreateService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new AccountingAssignmentValidator(db, Config()), new AccountingAssignmentDefaultsResolver(db, Config()), Guard(db));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(new FinancialDocument
        {
            CardCode = "C0001", BranchCode = "01", Direction = FinancialDirection.Receivable,
            DocumentDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), NetAmount = 100m, LedgerAccountCode = Ledger,
        }, "tester"));

        Assert.Equal($"A conta {Ledger} exige centro de custo.", ex.Message);
    }

    [Fact]
    public async Task Advance_without_contract_blocked_by_account()
    {
        var db = await NewDb();
        db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await db.SaveChangesAsync();
        var service = new FinancialAdvancesCreateService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
            new FinancialDocumentsSettleService(db),
            new AccountingAssignmentValidator(db, Config()), new AccountingAssignmentDefaultsResolver(db, Config()), Guard(db));

        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester",
            dimensions: new FinancialAccountEntry { LedgerAccountCode = Ledger }));
        Assert.Empty(db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Account_entry_blocked_by_account_and_passes_with_cost_center()
    {
        var db = await NewDb();
        db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await db.SaveChangesAsync();
        var service = new FinancialAccountEntriesCreateService(db, new AccountingAssignmentValidator(db, Config()), Guard(db));

        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = Today, Amount = 10m, Description = "x", LedgerAccountCode = Ledger,
        }, "tester"));
        Assert.Empty(db.Context.FinancialAccountEntries);

        await service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = Today, Amount = 10m, Description = "x", LedgerAccountCode = Ledger,
            CostCenterCode = "CC01",
        }, "tester");
        Assert.Single(db.Context.FinancialAccountEntries);
    }
}
