using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

public class DimensionDefaultsResolverTests
{
    private sealed class Fields : IDimensionFields
    {
        public string? Dim1Code { get; set; }
        public string? Dim2Code { get; set; }
        public string? Dim3Code { get; set; }
        public string? Dim4Code { get; set; }
        public string? Dim5Code { get; set; }
        public string? LedgerAccountCode { get; set; }
    }

    private static IConfiguration Config(string erp = "STANDALONE") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    private static async Task<IUnitOfWork> NewDb(DimensionRole dim1Role = DimensionRole.None)
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        db.Context.AccountingDimensions.Single(x => x.Number == 1).Role = dim1Role;
        db.Context.CostCenters.Add(new CostCenter { Code = "HDR", Name = "Cabeçalho", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "PRT", Name = "Parceiro", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "USG", Name = "Natureza", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "TYP", Name = "Digitado", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "VEI", Name = "Veículo", DimensionNumber = 2 });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<int> AddUsage(IUnitOfWork db, string? dim1, string? dim2 = null, string? ledger = null)
    {
        var u = new Usage { Name = "Natureza", Dim1Code = dim1, Dim2Code = dim2, LedgerAccountCode = ledger };
        db.Context.Usages.Add(u);
        await db.SaveChangesAsync();
        return u.Code;
    }

    private static async Task AddPartner(IUnitOfWork db, string? defaultCode)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "P1", CardName = "Parceiro", DefaultDimensionCode = defaultCode });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Header_wins_over_partner_and_usage()
    {
        var db = await NewDb(DimensionRole.Partner);
        await AddPartner(db, "PRT");
        var usage = await AddUsage(db, "USG");
        var target = new Fields();

        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, new Fields { Dim1Code = "HDR" }, "P1", usage);

        Assert.Equal("HDR", target.Dim1Code);
    }

    [Fact]
    public async Task Partner_default_goes_to_partner_role_dimension_only()
    {
        var db = await NewDb(DimensionRole.None);
        await AddPartner(db, "PRT");
        var usage = await AddUsage(db, "USG");
        var target = new Fields();
        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, null, "P1", usage);
        Assert.Equal("USG", target.Dim1Code);

        var db2 = await NewDb(DimensionRole.Partner);
        await AddPartner(db2, "PRT");
        var usage2 = await AddUsage(db2, "USG");
        var target2 = new Fields();
        await new DimensionDefaultsResolver(db2, Config()).ApplyAsync(target2, null, "P1", usage2);
        Assert.Equal("PRT", target2.Dim1Code);
    }

    [Fact]
    public async Task Default_matches_cost_center_stored_in_lowercase()
    {
        var db = await NewDb();
        db.Context.CostCenters.Add(new CostCenter { Code = "low", Name = "Legado", DimensionNumber = 1 });
        await db.SaveChangesAsync();
        var usage = await AddUsage(db, "low");
        var target = new Fields();

        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, null, null, usage);

        Assert.Equal("LOW", target.Dim1Code);
    }

    [Fact]
    public async Task Typed_value_is_never_overwritten()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, "USG", ledger: "1.2");
        var target = new Fields { Dim1Code = "TYP", LedgerAccountCode = "9.9" };

        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, new Fields { Dim1Code = "HDR" }, null, usage);

        Assert.Equal("TYP", target.Dim1Code);
        Assert.Equal("9.9", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Invalid_partner_default_falls_through_to_usage()
    {
        var db = await NewDb(DimensionRole.Partner);
        await AddPartner(db, "VEI"); // código da dimensão 2, não da 1
        var usage = await AddUsage(db, "USG");
        var target = new Fields();

        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, null, "P1", usage);

        Assert.Equal("USG", target.Dim1Code);
    }

    [Fact]
    public async Task Inactive_dimension_is_not_filled()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, "USG", dim2: "VEI");
        var target = new Fields();

        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, null, null, usage);

        Assert.Equal("USG", target.Dim1Code);
        Assert.Null(target.Dim2Code);
    }

    [Fact]
    public async Task Ledger_account_from_usage()
    {
        var db = await NewDb();
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.2", Name = "Usage", AllowsPosting = true });
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "H.1", Name = "Header", AllowsPosting = true });
        await db.SaveChangesAsync();
        var usage = await AddUsage(db, null, ledger: "1.2");
        var target = new Fields();
        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(target, null, null, usage);
        Assert.Equal("1.2", target.LedgerAccountCode);

        var t2 = new Fields();
        await new DimensionDefaultsResolver(db, Config()).ApplyAsync(t2, new Fields { LedgerAccountCode = "H.1" }, null, usage);
        Assert.Equal("H.1", t2.LedgerAccountCode);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, "USG");
        var target = new Fields();
        await new DimensionDefaultsResolver(db, Config("SAPB1")).ApplyAsync(target, null, null, usage);
        Assert.Null(target.Dim1Code);
    }
}
