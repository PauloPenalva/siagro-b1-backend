using SiagroB1.Application.Services.SpedFiscal;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.SpedFiscal;

public class SpedFiscalSettingsServiceTests
{
    private const string ValidCpf = "529.982.247-25";

    private static SpedFiscalSettings Valid(string branch = "1") => new()
    {
        BranchCode = branch,
        Profile = "A",
        ActivityType = 1,
        AccountantName = "Maria Contadora",
        AccountantCpf = ValidCpf,
        AccountantCrc = "PR-012345/O",
        IcmsRevenueCode = "100129",
        IcmsDueDay = 10,
    };

    [Fact]
    public async Task Save_CreatesThenUpdates()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new SpedFiscalSettingsService(db);

        Assert.Null(await service.GetAsync("1"));

        var created = await service.SaveAsync(Valid(), "admin");
        Assert.Equal("52998224725", created.AccountantCpf);
        Assert.Equal("admin", created.UpdatedBy);

        var changed = Valid();
        changed.Profile = "B";
        changed.IcmsDueDay = 25;
        await service.SaveAsync(changed, "outro");

        Assert.Single(db.Context.SpedFiscalSettings);
        var row = await service.GetAsync("1");
        Assert.Equal("B", row!.Profile);
        Assert.Equal(25, row.IcmsDueDay);
        Assert.Equal("outro", row.UpdatedBy);
    }

    [Fact]
    public async Task Save_RejectsInvalidCpf()
    {
        var service = new SpedFiscalSettingsService(TestDb.CreateUnitOfWork());
        var s = Valid();
        s.AccountantCpf = "111.111.111-11";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(s, "admin"));
        Assert.Equal("CPF do contabilista inválido.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task Save_RejectsDueDayOutOfRange(int day)
    {
        var service = new SpedFiscalSettingsService(TestDb.CreateUnitOfWork());
        var s = Valid();
        s.IcmsDueDay = day;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(s, "admin"));
        Assert.Equal("Dia de vencimento deve estar entre 1 e 31.", ex.Message);
    }

    [Fact]
    public async Task Save_RejectsProfileOtherThanABC()
    {
        var service = new SpedFiscalSettingsService(TestDb.CreateUnitOfWork());
        var s = Valid();
        s.Profile = "D";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(s, "admin"));
        Assert.Equal("Perfil deve ser A, B ou C.", ex.Message);
    }
}
