using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Taxes;

public class RuralContributionRatesServiceTests
{
    private static RuralContributionRate Rate(
        RuralContributionScheme scheme, DateOnly start, decimal inss = 1.2m, decimal rat = 0.1m, decimal senar = 0.2m) => new()
    {
        Scheme = scheme, StartDate = start, InssRate = inss, RatRate = rat, SenarRate = senar,
    };

    [Fact]
    public async Task Effective_rate_is_the_latest_start_date_of_the_same_scheme()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new RuralContributionRatesService(db);
        await service.CreateAsync(Rate(RuralContributionScheme.Standard, new DateOnly(2025, 1, 1), inss: 1m));
        await service.CreateAsync(Rate(RuralContributionScheme.Standard, new DateOnly(2026, 1, 1), inss: 2m));
        await service.CreateAsync(Rate(RuralContributionScheme.PayrollOption, new DateOnly(2026, 6, 1), inss: 3m));

        var standard = await service.GetEffectiveAsync(RuralContributionScheme.Standard, new DateOnly(2026, 3, 1));
        Assert.Equal(2m, standard!.InssRate);
        Assert.Null(await service.GetEffectiveAsync(RuralContributionScheme.PayrollOption, new DateOnly(2026, 3, 1)));
    }

    [Theory]
    [InlineData(-1, 0.1, 0.2, "INSS")]
    [InlineData(1, 100.5, 0.2, "RAT")]
    [InlineData(1, 0.1, -0.01, "SENAR")]
    public async Task Rate_outside_0_100_is_refused(double inss, double rat, double senar, string label)
    {
        var db = TestDb.CreateUnitOfWork();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new RuralContributionRatesService(db).CreateAsync(Rate(
                RuralContributionScheme.Standard, new DateOnly(2026, 1, 1), (decimal)inss, (decimal)rat, (decimal)senar)));
        Assert.Equal($"Alíquota de {label} deve estar entre 0 e 100.", ex.Message);
    }

    [Theory]
    [InlineData(RuralContributionScheme.Standard, "Comercialização")]
    [InlineData(RuralContributionScheme.PayrollOption, "Optante pela folha")]
    public async Task Same_scheme_and_start_date_is_refused(RuralContributionScheme scheme, string label)
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new RuralContributionRatesService(db);
        await service.CreateAsync(Rate(scheme, new DateOnly(2026, 1, 1)));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rate(scheme, new DateOnly(2026, 1, 1))));
        Assert.Equal($"Já existe uma vigência de contribuição social ({label}) iniciando em 01/01/2026.", ex.Message);
    }

    [Fact]
    public async Task Same_start_date_in_another_scheme_is_accepted()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new RuralContributionRatesService(db);
        await service.CreateAsync(Rate(RuralContributionScheme.Standard, new DateOnly(2026, 1, 1)));

        var created = await service.CreateAsync(Rate(RuralContributionScheme.PayrollOption, new DateOnly(2026, 1, 1)));
        Assert.NotEqual(0, created.Key);
    }

    [Fact]
    public async Task Update_and_delete_work()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new RuralContributionRatesService(db);
        var created = await service.CreateAsync(Rate(RuralContributionScheme.Standard, new DateOnly(2026, 1, 1)));

        created.InssRate = 5m;
        await service.UpdateAsync(created.Key, created);
        Assert.Equal(5m, (await service.GetByIdAsync(created.Key))!.InssRate);

        Assert.True(await service.DeleteAsync(created.Key));
        Assert.Null(await service.GetByIdAsync(created.Key));
    }
}
