using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.UsersProfiles;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Security;

/// <summary>Perfil sem empresa informada vale para a empresa da SESSÃO de quem cadastra; o mesmo perfil pode repetir em outra empresa, não na mesma.</summary>
public class UsersProfilesCreateServiceTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _userId;

    public UsersProfilesCreateServiceTests()
    {
        var user = new User { Username = "joao", FullName = "João" };
        _db.Users.Add(user);
        _userId = user.Id;
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
        _db.SaveChanges();
    }

    private UsersProfilesCreateService Create(string sessionCompany = "0001") =>
        new(_db, new FakeCompanyContext(sessionCompany), new FakeStringLocalizer<Resource>(), NullLogger<UsersProfilesCreateService>.Instance);

    [Fact]
    public async Task Missing_company_defaults_to_the_session_company()
    {
        var saved = await Create("0002").ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER" });

        Assert.Equal("0002", saved.CompanyCode);
    }

    [Fact]
    public async Task Unknown_company_is_refused()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "9999" }));
        Assert.Equal("Empresa inválida.", ex.Message);
    }

    [Fact]
    public async Task Same_profile_twice_in_the_same_company_is_refused_but_allowed_in_another()
    {
        await Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0001" });

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0001" }));
        Assert.Equal("Este perfil já está atribuído ao usuário nesta empresa.", ex.Message);

        await Create().ExecuteAsync(_userId, new UserProfile { ProfileCode = "OPER", CompanyCode = "0002" });
        Assert.Equal(2, _db.UserProfiles.Count());
    }
}
