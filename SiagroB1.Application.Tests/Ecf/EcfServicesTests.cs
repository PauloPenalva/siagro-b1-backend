using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Ecf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Ecf;

internal static class EcfTestDb
{
    public static IUnitOfWork WithBranches(params string[] codes)
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var c in codes.DefaultIfEmpty("1")) db.Context.Branchs.Add(new Branch { Code = c, BranchName = "Filial " + c });
        db.SaveChangesAsync().GetAwaiter().GetResult();
        return db;
    }
}

public class EcfSettingsServiceTests
{
    private static EcfSettings Valid() => new()
    {
        BranchCode = "1", AssessmentForm = EcfAssessmentForm.Quarterly, MainCnae = "0111-3/01", LegalEntityQualification = "01",
    };

    [Fact]
    public async Task Save_creates_then_updates_and_normalizes_cnae()
    {
        var db = EcfTestDb.WithBranches();
        var service = new EcfSettingsService(db);
        Assert.Null(await service.GetAsync("1"));
        var saved = await service.SaveAsync(Valid(), "admin");
        Assert.Equal("0111301", saved.MainCnae);
        Assert.Equal("admin", saved.UpdatedBy);

        var changed = Valid(); changed.AssessmentForm = EcfAssessmentForm.Annual; changed.PreviousEcfHash = " abc ";
        await service.SaveAsync(changed, "outro");
        Assert.Single(db.Context.EcfSettings);
        var row = await service.GetAsync("1");
        Assert.Equal(EcfAssessmentForm.Annual, row!.AssessmentForm);
        Assert.Equal("abc", row.PreviousEcfHash);
    }

    [Theory]
    [InlineData("123456", "01", "CNAE deve ter 7 dígitos.")]
    [InlineData("0111301", "1", "Qualificação da pessoa jurídica deve ter 2 dígitos.")]
    [InlineData("0111301", "01", "Natureza jurídica deve ter 4 dígitos.", "206")]
    public async Task Save_rejects_invalid(string cnae, string qualification, string message, string nature = "2062")
    {
        var s = Valid(); s.MainCnae = cnae; s.LegalEntityQualification = qualification; s.NatureCode = nature;
        var db = EcfTestDb.WithBranches();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => new EcfSettingsService(db).SaveAsync(s, "a"));
        Assert.Equal(message, ex.Message);
        Assert.Empty(db.Context.EcfSettings);
    }
}

public class EcfPartnerServiceTests
{
    private static EcfPartner Valid() => new()
    {
        BranchCode = "1", Name = "Sócio", TaxId = "529.982.247-25", Qualification = "49",
        VotingCapitalPercent = 50, TotalCapitalPercent = 50,
    };

    private static EcfPartnerService Service(out IUnitOfWork db)
    {
        db = EcfTestDb.WithBranches();
        return new EcfPartnerService(db.Context, NullLogger<EcfPartnerService>.Instance);
    }

    [Fact]
    public async Task Create_normalizes_tax_id_and_defaults_country()
    {
        var service = Service(out var db);
        await service.CreateAsync(Valid());
        var row = db.Context.EcfPartners.Single();
        Assert.Equal("52998224725", row.TaxId);
        Assert.Equal("105", row.CountryCode);
    }

    [Fact]
    public async Task Create_rejects_invalid_tax_id()
    {
        var p = Valid(); p.TaxId = "11111111111";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(out _).CreateAsync(p));
        Assert.Equal("CPF/CNPJ do sócio inválido.", ex.Message);
    }

    [Fact]
    public async Task Create_rejects_bad_qualification()
    {
        var p = Valid(); p.Qualification = "4";
        await Assert.ThrowsAsync<BusinessException>(() => Service(out _).CreateAsync(p));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 100.01)]
    public async Task Create_rejects_percent_out_of_range(double voting, double total)
    {
        var p = Valid(); p.VotingCapitalPercent = (decimal)voting; p.TotalCapitalPercent = (decimal)total;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(out _).CreateAsync(p));
        Assert.Equal("Percentual deve estar entre 0 e 100.", ex.Message);
    }

    [Fact]
    public async Task Update_validates_too()
    {
        var service = Service(out _);
        var p = await service.CreateAsync(Valid());
        p.TotalCapitalPercent = 150;
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(p.Id, p));
    }
}

public class EcfAdjustmentServiceTests
{
    private static EcfAdjustment Valid(int period = 0) => new()
    {
        BranchCode = "1", Year = 2025, Period = period, Tax = EcfAdjustmentTax.Both, Kind = EcfAdjustmentKind.Addition,
        TableCode = "M300.001", Description = "Multas", Amount = 100,
    };

    private static EcfAdjustmentService Service(IUnitOfWork db) =>
        new(db.Context, NullLogger<EcfAdjustmentService>.Instance);

    [Fact]
    public async Task Create_without_settings_accepts_any_valid_period()
    {
        var db = EcfTestDb.WithBranches();
        await Service(db).CreateAsync(Valid(3));
        Assert.Single(db.Context.EcfAdjustments);
    }

    [Theory]
    [InlineData(0, "Annual", true)]
    [InlineData(2, "Annual", false)]
    [InlineData(0, "Quarterly", false)]
    [InlineData(4, "Quarterly", true)]
    [InlineData(5, "Quarterly", false)]
    public async Task Period_follows_assessment_form(int period, string form, bool ok)
    {
        var db = EcfTestDb.WithBranches();
        db.Context.EcfSettings.Add(new EcfSettings { BranchCode = "1", AssessmentForm = Enum.Parse<EcfAssessmentForm>(form), MainCnae = "0111301" });
        await db.SaveChangesAsync();
        var task = Service(db).CreateAsync(Valid(period));
        if (ok) await task;
        else Assert.Equal("Período inválido para a forma de apuração.", (await Assert.ThrowsAsync<BusinessException>(() => task)).Message);
    }

    [Fact]
    public async Task Rejects_non_positive_amount_and_missing_table_code()
    {
        var db = EcfTestDb.WithBranches();
        var a = Valid(); a.Amount = 0;
        Assert.Equal("Valor do ajuste deve ser maior que zero.",
            (await Assert.ThrowsAsync<BusinessException>(() => Service(db).CreateAsync(a))).Message);
        var b = Valid(); b.TableCode = " ";
        Assert.Equal("Informe o código da tabela do ajuste.",
            (await Assert.ThrowsAsync<BusinessException>(() => Service(db).CreateAsync(b))).Message);
    }
}

public class EcfBranchAndLimitsTests
{
    private static EcfPartner Partner(string branch = "1") => new()
    {
        BranchCode = branch, Name = "Sócio", TaxId = "52998224725", Qualification = "49", VotingCapitalPercent = 10, TotalCapitalPercent = 10,
    };

    private static EcfPartnerService PartnerService(IUnitOfWork db) => new(db.Context, NullLogger<EcfPartnerService>.Instance);
    private static EcfAdjustmentService AdjService(IUnitOfWork db) => new(db.Context, NullLogger<EcfAdjustmentService>.Instance);

    [Fact]
    public async Task Unknown_branch_is_rejected_everywhere()
    {
        var db = EcfTestDb.WithBranches();
        Assert.Equal("Filial 9 não encontrada.", (await Assert.ThrowsAsync<BusinessException>(() => PartnerService(db).CreateAsync(Partner("9")))).Message);
        Assert.Equal("Filial 9 não encontrada.", (await Assert.ThrowsAsync<BusinessException>(() => AdjService(db).CreateAsync(
            new EcfAdjustment { BranchCode = "9", Year = 2025, TableCode = "T", Amount = 1 }))).Message);
        Assert.Equal("Filial 9 não encontrada.", (await Assert.ThrowsAsync<BusinessException>(() => new EcfSettingsService(db).SaveAsync(
            new EcfSettings { BranchCode = "9", MainCnae = "0111301", LegalEntityQualification = "01" }, "a"))).Message);
    }

    [Fact]
    public async Task Update_cannot_change_branch_and_missing_key_is_not_found()
    {
        var db = EcfTestDb.WithBranches("1", "2");
        var created = await PartnerService(db).CreateAsync(Partner());
        var moved = Partner("2"); moved.Id = created.Id;
        Assert.Equal("A filial não pode ser alterada.", (await Assert.ThrowsAsync<BusinessException>(() => PartnerService(db).UpdateAsync(created.Id, moved))).Message);

        var adj = await AdjService(db).CreateAsync(new EcfAdjustment { BranchCode = "1", Year = 2025, TableCode = "T", Amount = 1 });
        var movedAdj = new EcfAdjustment { Id = adj.Id, BranchCode = "2", Year = 2025, TableCode = "T", Amount = 1 };
        await Assert.ThrowsAsync<BusinessException>(() => AdjService(db).UpdateAsync(adj.Id, movedAdj));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => PartnerService(db).UpdateAsync(Guid.NewGuid(), Partner()));
    }

    [Fact]
    public async Task Max_lengths_and_country_code_are_validated()
    {
        var db = EcfTestDb.WithBranches();
        var p = Partner(); p.Name = new string('a', 101);
        await Assert.ThrowsAsync<BusinessException>(() => PartnerService(db).CreateAsync(p));
        var c = Partner(); c.CountryCode = "BR";
        Assert.Equal("Código do país deve ter 3 dígitos.", (await Assert.ThrowsAsync<BusinessException>(() => PartnerService(db).CreateAsync(c))).Message);

        var a = new EcfAdjustment { BranchCode = "1", Year = 2025, TableCode = new string('T', 21), Amount = 1 };
        await Assert.ThrowsAsync<BusinessException>(() => AdjService(db).CreateAsync(a));
        var d = new EcfAdjustment { BranchCode = "1", Year = 2025, TableCode = "T", Amount = 1, Description = new string('d', 251) };
        await Assert.ThrowsAsync<BusinessException>(() => AdjService(db).CreateAsync(d));

        var s = new EcfSettings { BranchCode = "1", MainCnae = "0111301", LegalEntityQualification = "01", PreviousEcfHash = new string('h', 41) };
        await Assert.ThrowsAsync<BusinessException>(() => new EcfSettingsService(db).SaveAsync(s, "a"));
    }
}
