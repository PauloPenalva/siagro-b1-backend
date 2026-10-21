using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Amarração contábil em título manual, adiantamento e lançamento na conta (STANDALONE).</summary>
public class FinancialAccountingAssignmentTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private async Task SeedAllAsync()
    {
        await SeedAsync(_db);
        await AddRuleAsync(_db, Rule("R01", Today.AddYears(-1), ("CC01", null, 100)));
        _db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C0001", CardName = "CLIENTE", DefaultCostCenterCode = "CC02" });
        _db.Context.FinancialAccounts.Add(new FinancialAccount { Code = "BCO1", Name = "Banco", Currency = CurrencyType.Brl });
        await _db.SaveChangesAsync();
    }

    private static FakeBusinessPartnerService Partners() =>
        new(new Dictionary<string, string> { ["C0001"] = "CLIENTE" });

    private FinancialDocumentsManualCreateService Create() =>
        new(_db, _docNumbers, Partners(), new AccountingAssignmentValidator(_db, Config()),
            new AccountingAssignmentDefaultsResolver(_db, Config()));

    private FinancialDocumentsManualUpdateService Update() =>
        new(_db, Partners(), new FinancialDocumentChangeLogService(_db.Context), new AccountingAssignmentValidator(_db, Config()));

    private static FinancialDocument Input() => new()
    {
        CardCode = "C0001", BranchCode = "01", Direction = FinancialDirection.Receivable,
        DocumentDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), NetAmount = 100m,
    };

    [Fact]
    public async Task Manual_title_gets_partner_default()
    {
        await SeedAllAsync();
        var doc = await Create().ExecuteAsync(Input(), "tester");
        Assert.Equal("CC02", doc.CostCenterCode);
    }

    [Fact]
    public async Task Manual_title_normalizes_and_rejects_invalid_code()
    {
        await SeedAllAsync();
        var ok = Input();
        ok.CostCenterCode = " cc01 ";
        Assert.Equal("CC01", (await Create().ExecuteAsync(ok, "tester")).CostCenterCode);

        var bad = Input();
        bad.CostCenterCode = "SYN";
        await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync(bad, "tester"));
    }

    [Fact]
    public async Task Manual_title_update_switches_mode_without_keeping_both()
    {
        await SeedAllAsync();
        var doc = await Create().ExecuteAsync(Input(), "tester");

        var ruled = await Update().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { DistributionRuleCode = "r01" }, "tester");
        Assert.Equal("R01", ruled.DistributionRuleCode);
        Assert.Null(ruled.CostCenterCode);

        var back = await Update().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { CostCenterCode = "CC01" }, "tester");
        Assert.Equal("CC01", back.CostCenterCode);
        Assert.Null(back.DistributionRuleCode);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Update().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { CostCenterCode = "ZZZ" }, "tester"));
    }

    [Fact]
    public async Task Old_title_with_since_inactivated_center_can_still_be_updated()
    {
        await SeedAllAsync();
        var input = Input();
        input.CostCenterCode = "CC01";
        var doc = await Create().ExecuteAsync(input, "tester");
        _db.Context.CostCenters.Single(x => x.Code == "CC01").Inactive = true;
        await _db.SaveChangesAsync();

        var updated = await Update().ExecuteAsync(doc.Key,
            new ManualFinancialDocumentChanges { CostCenterCode = "CC01", HasComments = true, Comments = "obs" }, "tester");

        Assert.Equal("CC01", updated.CostCenterCode);
        Assert.Equal("obs", updated.Comments);
    }

    [Fact]
    public async Task Advance_without_contract_validates_assignment()
    {
        await SeedAllAsync();
        var service = new FinancialAdvancesCreateService(_db, _docNumbers, Partners(), new FinancialDocumentsSettleService(_db),
            new AccountingAssignmentValidator(_db, Config()), new AccountingAssignmentDefaultsResolver(_db, Config()));

        var advance = await service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester");
        Assert.Equal("CC02", advance.CostCenterCode);

        var bad = new FinancialAccountEntry { CostCenterCode = "CC01", DistributionRuleCode = "R01" };
        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "C0001", "01", 50m, DateTime.Today, "BCO1", DateTime.Today, null, "tester",
            dimensions: bad));
        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Account_entry_validity_uses_entry_date()
    {
        await SeedAllAsync();
        var service = new FinancialAccountEntriesCreateService(_db, new AccountingAssignmentValidator(_db, Config()));

        // EXP venceu ontem: lançamento datado antes aceita; datado hoje recusa.
        var created = await service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = Today.AddDays(-5), Amount = 10m, Description = "x", CostCenterCode = "exp",
        }, "tester");
        Assert.Equal("EXP", created.CostCenterCode);

        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = Today, Amount = 10m, Description = "x", CostCenterCode = "EXP",
        }, "tester"));
    }
}
