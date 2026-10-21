using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

public class DimensionCodeValidatorTests
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

    private static IConfiguration Config(string erp) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    private static async Task<IUnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        db.Context.CostCenters.Add(new CostCenter { Code = "CC01", Name = "Centro", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "OLD1", Name = "Antigo", DimensionNumber = 1, Inactive = true });
        db.Context.CostCenters.Add(new CostCenter { Code = "VEI1", Name = "Caminhão", DimensionNumber = 2 });
        db.Context.DistributionRules.Add(new DistributionRule { Code = "R01", Name = "Rateio", DimensionNumber = 1 });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Accepts_active_center_and_rule_of_dimension()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        await v.NormalizeAndValidateAsync(new Fields { Dim1Code = "CC01" });
        await v.NormalizeAndValidateAsync(new Fields { Dim1Code = "R01" });
    }

    [Fact]
    public async Task Rejects_code_of_other_dimension()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            v.NormalizeAndValidateAsync(new Fields { Dim1Code = "VEI1" }));
        Assert.Equal("O código VEI1 não é centro de custo nem regra de distribuição ativa da dimensão Dimensão 1.", ex.Message);
    }

    [Fact]
    public async Task Rejects_inactive_dimension()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        // Dimensão 2 semeada inativa.
        await Assert.ThrowsAsync<DefaultException>(() =>
            v.NormalizeAndValidateAsync(new Fields { Dim2Code = "VEI1" }));
    }

    [Fact]
    public async Task Normalizes_lowercase_and_spaces()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));
        var f = new Fields { Dim1Code = " cc01 ", Dim2Code = "   ", LedgerAccountCode = "  " };

        await v.NormalizeAndValidateAsync(f);

        Assert.Equal("CC01", f.Dim1Code);
        Assert.Null(f.Dim2Code);
        Assert.Null(f.LedgerAccountCode);
    }

    [Fact]
    public async Task Accepts_code_stored_in_lowercase()
    {
        var db = await NewDb();
        db.Context.CostCenters.Add(new CostCenter { Code = "cc9", Name = "Legado", DimensionNumber = 1 });
        await db.SaveChangesAsync();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        await v.NormalizeAndValidateAsync(new Fields { Dim1Code = "cc9" });
    }

    [Fact]
    public async Task Unchanged_inactive_code_is_accepted_when_previous_given()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        await v.NormalizeAndValidateAsync(new Fields { Dim1Code = "old1" }, new Fields { Dim1Code = "OLD1" });

        await Assert.ThrowsAsync<DefaultException>(() =>
            v.NormalizeAndValidateAsync(new Fields { Dim1Code = "OLD1" }));
    }

    [Fact]
    public async Task Rejects_non_posting_ledger_account()
    {
        var db = await NewDb();
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1", Name = "Sintética", AllowsPosting = false });
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.2", Name = "Analítica", AllowsPosting = true });
        await db.SaveChangesAsync();
        var v = new DimensionCodeValidator(db, Config("STANDALONE"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            v.NormalizeAndValidateAsync(new Fields { LedgerAccountCode = "1.1" }));
        Assert.Equal("A conta contábil 1.1 não existe, está inativa ou não aceita lançamento.", ex.Message);

        await v.NormalizeAndValidateAsync(new Fields { LedgerAccountCode = " 1.2 " });
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb();
        var v = new DimensionCodeValidator(db, Config("SAPB1"));
        var f = new Fields { Dim1Code = " zzz " };

        await v.NormalizeAndValidateAsync(f);

        Assert.Equal(" zzz ", f.Dim1Code);
    }
}
