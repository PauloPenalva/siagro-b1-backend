using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Dimensions;

public class DimensionSchemaTests
{
    [Fact]
    public async Task Persists_dimension_fields_on_all_documents()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);

        var salesItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
            Dim1Code = "CC01", Dim5Code = "R5", LedgerAccountCode = "4.1.01",
        };
        var purchaseItem = new PurchaseInvoiceItem { Key = Guid.NewGuid(), Dim1Code = "CC01", Dim5Code = "R5" };
        var financialDocument = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FD-1", CardCode = "F0001", Dim1Code = "CC01", Dim5Code = "R5",
        };
        var entry = new FinancialAccountEntry { Key = Guid.NewGuid(), Dim1Code = "CC01", Dim5Code = "R5" };

        db.Context.SalesInvoicesItems.Add(salesItem);
        db.Context.PurchaseInvoicesItems.Add(purchaseItem);
        db.Context.FinancialDocuments.Add(financialDocument);
        db.Context.FinancialAccountEntries.Add(entry);
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name);
        IDimensionFields[] loaded =
        [
            await read.Context.SalesInvoicesItems.SingleAsync(x => x.Key == salesItem.Key),
            await read.Context.PurchaseInvoicesItems.SingleAsync(x => x.Key == purchaseItem.Key),
            await read.Context.FinancialDocuments.SingleAsync(x => x.Key == financialDocument.Key),
            await read.Context.FinancialAccountEntries.SingleAsync(x => x.Key == entry.Key),
        ];

        foreach (var f in loaded)
        {
            Assert.Equal("CC01", f.Dim1Code);
            Assert.Equal("R5", f.Dim5Code);
            Assert.Equal("CC01", f.GetDim(1));
            Assert.Equal("R5", f.GetDim(5));
            Assert.Null(f.GetDim(3));
        }
        Assert.Equal("4.1.01", loaded[0].LedgerAccountCode);
    }

    [Fact]
    public void SetDim_writes_the_matching_slot()
    {
        IDimensionFields f = new FinancialAccountEntry();
        for (var n = 1; n <= 5; n++) f.SetDim(n, "D" + n);
        Assert.Equal("D1", f.Dim1Code);
        Assert.Equal("D2", f.Dim2Code);
        Assert.Equal("D3", f.Dim3Code);
        Assert.Equal("D4", f.Dim4Code);
        Assert.Equal("D5", f.Dim5Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.SetDim(6, "x"));
    }

    [Fact]
    public async Task Rule_version_lines_cascade()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var version = new DistributionRuleVersion
        {
            Id = Guid.NewGuid(), RuleCode = "R1", ValidFrom = new DateTime(2026, 1, 1),
            Lines =
            [
                new DistributionRuleLine { Id = Guid.NewGuid(), CostCenterCode = "CC01", Percentage = 60m },
                new DistributionRuleLine { Id = Guid.NewGuid(), CostCenterCode = "CC02", Percentage = 40m },
            ],
        };
        db.Context.DistributionRules.Add(new DistributionRule
        {
            Code = "R1", Name = "Regra 1", DimensionNumber = 1, Versions = [version],
        });
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name);
        var rule = await read.Context.DistributionRules
            .Include(r => r.Versions).ThenInclude(v => v.Lines)
            .SingleAsync(r => r.Code == "R1");

        var v = Assert.Single(rule.Versions);
        Assert.Equal(2, v.Lines.Count);
        Assert.Equal(100m, v.Lines.Sum(l => l.Percentage));
    }

    [Fact]
    public void Model_seeds_dimensions_and_company_settings()
    {
        // InMemory só semeia via EnsureCreated; confere a semente no modelo (é ela que a migration replica).
        var db = TestDb.CreateUnitOfWork();
        var model = db.Context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;

        var dims = model.FindEntityType(typeof(AccountingDimension))!.GetSeedData().ToList();
        Assert.Equal(5, dims.Count);
        Assert.Equal(true, dims.Single(d => (int)d["Number"]! == 1)["Active"]);
        Assert.All(dims.Where(d => (int)d["Number"]! > 1), d => Assert.Equal(false, d["Active"]));

        var settings = Assert.Single(model.FindEntityType(typeof(CompanySettings))!.GetSeedData());
        Assert.Equal(1, settings["Id"]);
        Assert.Equal(false, settings["RequireCostCenter"]);
        Assert.Equal(false, settings["RequireLedgerAccount"]);
    }
}
