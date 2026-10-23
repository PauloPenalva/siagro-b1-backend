using SiagroB1.Application.Services.Ecd;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Ecd;

public class EcdSettingsServiceTests
{
    private static EcdSettings Valid(string branch = "1") => new()
    {
        BranchCode = branch,
        BookNumber = 1,
        ReferentialPlanCode = "1",
        SignerName = "João Diretor",
        SignerCpf = "529.982.247-25",
        SignerQualificationCode = "203",
        SignerEmail = "diretor@empresa.com.br",
    };

    [Fact]
    public async Task Save_CreatesThenUpdates()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new EcdSettingsService(db);
        Assert.Null(await service.GetAsync("1"));

        var created = await service.SaveAsync(Valid(), "admin");
        Assert.Equal("52998224725", created.SignerCpf);
        Assert.Equal("admin", created.UpdatedBy);

        var changed = Valid();
        changed.BookNumber = 2;
        changed.HasNire = true;
        changed.Nire = "41.2012345-60";
        await service.SaveAsync(changed, "outro");

        Assert.Single(db.Context.EcdSettings);
        var row = await service.GetAsync("1");
        Assert.Equal(2, row!.BookNumber);
        Assert.Equal("41201234560", row.Nire);
        Assert.Equal("outro", row.UpdatedBy);
    }

    [Fact]
    public async Task Save_RejectsBookNumberBelowOne()
    {
        var s = Valid(); s.BookNumber = 0;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => new EcdSettingsService(TestDb.CreateUnitOfWork()).SaveAsync(s, "a"));
        Assert.Equal("Número do livro deve ser maior que zero.", ex.Message);
    }

    [Theory]
    [InlineData("123")]
    [InlineData(null)]
    public async Task Save_RejectsNireWithoutElevenDigits(string? nire)
    {
        var s = Valid(); s.HasNire = true; s.Nire = nire;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => new EcdSettingsService(TestDb.CreateUnitOfWork()).SaveAsync(s, "a"));
        Assert.Equal("NIRE deve ter 11 dígitos.", ex.Message);
    }

    [Fact]
    public async Task Save_IgnoresNireWhenHasNireFalse()
    {
        var s = Valid(); s.HasNire = false; s.Nire = "123";
        var saved = await new EcdSettingsService(TestDb.CreateUnitOfWork()).SaveAsync(s, "a");
        Assert.Null(saved.Nire);
    }

    [Fact]
    public async Task Save_RejectsInvalidCpf()
    {
        var s = Valid(); s.SignerCpf = "111.111.111-11";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => new EcdSettingsService(TestDb.CreateUnitOfWork()).SaveAsync(s, "a"));
        Assert.Equal("CPF do responsável inválido.", ex.Message);
    }

    [Theory]
    [InlineData("20")]
    [InlineData("2031")]
    [InlineData("abc")]
    public async Task Save_RejectsQualificationWithoutThreeDigits(string code)
    {
        var s = Valid(); s.SignerQualificationCode = code;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => new EcdSettingsService(TestDb.CreateUnitOfWork()).SaveAsync(s, "a"));
        Assert.Equal("Qualificação do assinante deve ter 3 dígitos.", ex.Message);
    }
}
