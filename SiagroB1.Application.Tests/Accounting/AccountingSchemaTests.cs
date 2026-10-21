using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Esquema da amarração contábil (CC + Item Contábil + rateio + conta) no modelo do Protheus.</summary>
public class AccountingSchemaTests
{
    private static void Fill(IAccountingAssignment target, int i)
    {
        target.CostCenterCode = "CC" + i;
        target.AccountingItemCode = "IT" + i;
        target.DistributionRuleCode = "R" + i;
        target.LedgerAccountCode = "4.1." + i;
    }

    [Fact]
    public async Task Persists_assignment_on_all_documents()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);

        var sales = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        var salesItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        };
        var purchase = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        var purchaseItem = new PurchaseInvoiceItem { Key = Guid.NewGuid() };
        var financialDocument = new FinancialDocument { Key = Guid.NewGuid(), Code = "FD-1", CardCode = "F1" };
        var entry = new FinancialAccountEntry { Key = Guid.NewGuid() };
        var usage = new Usage { Name = "VENDA" };

        IAccountingAssignment[] all = [sales, salesItem, purchase, purchaseItem, financialDocument, entry, usage];
        for (var i = 0; i < all.Length; i++) Fill(all[i], i);

        db.Context.SalesInvoices.Add(sales);
        db.Context.SalesInvoicesItems.Add(salesItem);
        db.Context.PurchaseInvoices.Add(purchase);
        db.Context.PurchaseInvoicesItems.Add(purchaseItem);
        db.Context.FinancialDocuments.Add(financialDocument);
        db.Context.FinancialAccountEntries.Add(entry);
        db.Context.Usages.Add(usage);
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name);
        IAccountingAssignment[] loaded =
        [
            await read.Context.SalesInvoices.SingleAsync(x => x.Key == sales.Key),
            await read.Context.SalesInvoicesItems.SingleAsync(x => x.Key == salesItem.Key),
            await read.Context.PurchaseInvoices.SingleAsync(x => x.Key == purchase.Key),
            await read.Context.PurchaseInvoicesItems.SingleAsync(x => x.Key == purchaseItem.Key),
            await read.Context.FinancialDocuments.SingleAsync(x => x.Key == financialDocument.Key),
            await read.Context.FinancialAccountEntries.SingleAsync(x => x.Key == entry.Key),
            await read.Context.Usages.SingleAsync(x => x.Code == usage.Code),
        ];

        for (var i = 0; i < loaded.Length; i++)
        {
            Assert.Equal("CC" + i, loaded[i].CostCenterCode);
            Assert.Equal("IT" + i, loaded[i].AccountingItemCode);
            Assert.Equal("R" + i, loaded[i].DistributionRuleCode);
            Assert.Equal("4.1." + i, loaded[i].LedgerAccountCode);
        }
    }

    [Fact]
    public async Task Rule_line_accepts_cost_center_and_item()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        db.Context.DistributionRules.Add(new DistributionRule
        {
            Code = "DIESEL", Name = "Diesel",
            Versions =
            [
                new DistributionRuleVersion
                {
                    Id = Guid.NewGuid(), RuleCode = "DIESEL", ValidFrom = new DateTime(2026, 1, 1),
                    Lines =
                    [
                        new DistributionRuleLine { Id = Guid.NewGuid(), CostCenterCode = "OPER", AccountingItemCode = "CAM01", Percentage = 60m },
                        new DistributionRuleLine { Id = Guid.NewGuid(), CostCenterCode = "OPER", AccountingItemCode = "CAM02", Percentage = 30m },
                        new DistributionRuleLine { Id = Guid.NewGuid(), AccountingItemCode = "CAM03", Percentage = 10m },
                    ],
                },
            ],
        });
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name);
        var rule = await read.Context.DistributionRules
            .Include(r => r.Versions).ThenInclude(v => v.Lines)
            .SingleAsync(r => r.Code == "DIESEL");

        var lines = Assert.Single(rule.Versions).Lines;
        Assert.Equal(3, lines.Count);
        Assert.Equal(100m, lines.Sum(l => l.Percentage));
        Assert.Contains(lines, l => l.CostCenterCode == "OPER" && l.AccountingItemCode == "CAM01");
        Assert.Contains(lines, l => l.CostCenterCode is null && l.AccountingItemCode == "CAM03");
    }

    [Fact]
    public async Task Ledger_account_flags_default_accept_true_require_false()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "4.1.01", Name = "Diesel" });
        await db.SaveChangesAsync();

        var account = await TestDb.CreateUnitOfWork(name).Context.LedgerAccounts.SingleAsync(x => x.Code == "4.1.01");
        Assert.True(account.AcceptsCostCenter);
        Assert.True(account.AcceptsAccountingItem);
        Assert.False(account.RequiresCostCenter);
        Assert.False(account.RequiresAccountingItem);
    }

    [Fact]
    public async Task Cost_center_and_accounting_item_default_to_analytic_without_parent()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        db.Context.CostCenters.Add(new CostCenter { Code = "OPER", Name = "Operação" });
        db.Context.AccountingItems.Add(new AccountingItem { Code = "CAM01", Name = "Caminhão 01" });
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name);
        var center = await read.Context.CostCenters.SingleAsync(x => x.Code == "OPER");
        var item = await read.Context.AccountingItems.SingleAsync(x => x.Code == "CAM01");
        Assert.Equal(AccountingEntityClass.Analytic, center.Class);
        Assert.Equal(AccountingEntityClass.Analytic, item.Class);
        Assert.Null(center.ParentCode);
        Assert.Null(item.ParentCode);
    }

    [Fact]
    public void Snapshot_copies_all_four_fields()
    {
        var source = new FinancialAccountEntry();
        Fill(source, 7);

        var snapshot = AccountingAssignmentSnapshot.From(source);
        var target = new FinancialAccountEntry();
        AccountingAssignmentSnapshot.Copy(snapshot, target);

        Assert.Equal("CC7", target.CostCenterCode);
        Assert.Equal("IT7", target.AccountingItemCode);
        Assert.Equal("R7", target.DistributionRuleCode);
        Assert.Equal("4.1.7", target.LedgerAccountCode);

        var original = AccountingAssignmentSnapshot.FromOriginal(
            n => n == nameof(IAccountingAssignment.CostCenterCode) ? "OLD" : null);
        Assert.Equal("OLD", original.CostCenterCode);
        Assert.Null(original.AccountingItemCode);
    }
}
