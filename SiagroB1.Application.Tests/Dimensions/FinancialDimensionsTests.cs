using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>Dimensões e conta contábil em título manual, adiantamento e lançamento na conta (STANDALONE).</summary>
public class FinancialDimensionsTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "STANDALONE" }).Build();

    private async Task SeedAsync()
    {
        await DimensionTestData.SeedDimensions(_db);
        _db.Context.AccountingDimensions.Single(x => x.Number == 1).Role = DimensionRole.Partner;
        _db.Context.CostCenters.Add(new CostCenter { Code = "PRT", Name = "Parceiro", DimensionNumber = 1 });
        _db.Context.CostCenters.Add(new CostCenter { Code = "NEW", Name = "Novo", DimensionNumber = 1 });
        _db.Context.CostCenters.Add(new CostCenter { Code = "OLD", Name = "Antigo", DimensionNumber = 1 });
        _db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C0001", CardName = "CLIENTE", DefaultDimensionCode = "PRT" });
        _db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await _db.SaveChangesAsync();
    }

    private static FakeBusinessPartnerService Partners() =>
        new(new Dictionary<string, string> { ["C0001"] = "CLIENTE" });

    private FinancialDocumentsManualCreateService Create() =>
        new(_db, _docNumbers, Partners(), new DimensionCodeValidator(_db, Config()), new DimensionDefaultsResolver(_db, Config()));

    private FinancialDocumentsManualUpdateService Update() =>
        new(_db, Partners(), new FinancialDocumentChangeLogService(_db.Context), new DimensionCodeValidator(_db, Config()));

    private static FinancialDocument Input() => new()
    {
        CardCode = "C0001", BranchCode = "01", Direction = FinancialDirection.Receivable,
        DocumentDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), NetAmount = 100m,
    };

    [Fact]
    public async Task Manual_title_gets_partner_default()
    {
        await SeedAsync();

        var doc = await Create().ExecuteAsync(Input(), "tester");

        Assert.Equal("PRT", doc.Dim1Code);
    }

    [Fact]
    public async Task Manual_title_normalizes_and_rejects_invalid_code()
    {
        await SeedAsync();
        var ok = Input();
        ok.Dim1Code = " new ";
        Assert.Equal("NEW", (await Create().ExecuteAsync(ok, "tester")).Dim1Code);

        var bad = Input();
        bad.Dim1Code = "ZZZ";
        await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(bad, "tester"));
    }

    [Fact]
    public async Task Manual_title_update_validates_changed_dims()
    {
        await SeedAsync();
        var doc = await Create().ExecuteAsync(Input(), "tester");

        var updated = await Update().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { Dim1Code = "new" }, "tester");
        Assert.Equal("NEW", updated.Dim1Code);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Update().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { Dim1Code = "ZZZ" }, "tester"));
    }

    [Fact]
    public async Task Old_title_with_since_inactivated_center_can_still_be_updated()
    {
        await SeedAsync();
        var input = Input();
        input.Dim1Code = "OLD";
        var doc = await Create().ExecuteAsync(input, "tester");
        _db.Context.CostCenters.Single(x => x.Code == "OLD").Inactive = true;
        await _db.SaveChangesAsync();

        var updated = await Update().ExecuteAsync(doc.Key,
            new ManualFinancialDocumentChanges { Dim1Code = "OLD", HasComments = true, Comments = "obs" }, "tester");

        Assert.Equal("OLD", updated.Dim1Code);
        Assert.Equal("obs", updated.Comments);
    }

    [Fact]
    public async Task Advance_without_contract_validates_dims()
    {
        await SeedAsync();
        var service = new FinancialAdvancesCreateService(_db, _docNumbers, Partners(), new FinancialDocumentsSettleService(_db),
            new DimensionCodeValidator(_db, Config()), new DimensionDefaultsResolver(_db, Config()));

        var advance = await service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester");
        Assert.Equal("PRT", advance.Dim1Code);

        var bad = new FinancialAccountEntry { Dim1Code = "ZZZ" };
        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester",
            dimensions: bad));
        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Account_entry_rejects_invalid_code()
    {
        await SeedAsync();
        var service = new FinancialAccountEntriesCreateService(_db, new DimensionCodeValidator(_db, Config()));

        var created = await service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = DateTime.Today, Amount = 10m, Description = "x", Dim1Code = "new",
        }, "tester");
        Assert.Equal("NEW", created.Dim1Code);

        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = DateTime.Today, Amount = 10m, Description = "x", Dim1Code = "ZZZ",
        }, "tester"));
    }
}
