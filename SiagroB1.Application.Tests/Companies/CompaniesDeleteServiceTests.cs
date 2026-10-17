using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompaniesDeleteServiceTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyCatalog _catalog = new() { DefaultCompanyCode = "0001" };

    public CompaniesDeleteServiceTests()
    {
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "A", ConnectionString = "a" });
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "B", ConnectionString = "b" });
        _db.SaveChanges();
    }

    private CompaniesDeleteService Create() =>
        new(_db, _catalog, new FakeStringLocalizer<Resource>(), NullLogger<CompaniesDeleteService>.Instance);

    [Fact]
    public async Task Default_company_cannot_be_deleted()
    {
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Create().ExecuteAsync("0001"));
        Assert.Contains("padrão", ex.Message);
    }

    [Fact]
    public async Task Company_with_profiles_or_active_sessions_cannot_be_deleted()
    {
        var user = new User { Username = "u", FullName = "u" };
        _db.Users.Add(user);
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "P", CompanyCode = "0002" });
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Create().ExecuteAsync("0002"));
        Assert.Contains("perfis", ex.Message);
    }

    [Fact]
    public async Task Unused_company_is_deleted_and_the_catalog_invalidated()
    {
        await Create().ExecuteAsync("0002");

        Assert.Single(_db.Companies);
        Assert.Equal(1, _catalog.Invalidations);
    }

    private void SeedSession(DateTime expiresAt)
    {
        var user = new User { Username = "s", FullName = "s" };
        _db.Users.Add(user);
        _db.UserSessions.Add(new UserSession
        {
            SessionId = Guid.NewGuid().ToString(), UserId = user.Id, IsActive = true,
            ExpiresAt = expiresAt, CompanyCode = "0002"
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Company_with_a_live_session_cannot_be_deleted()
    {
        SeedSession(DateTime.UtcNow.AddHours(1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Create().ExecuteAsync("0002"));
        Assert.Contains("sessões", ex.Message);
    }

    [Fact]
    public async Task Company_with_only_expired_sessions_can_be_deleted()
    {
        SeedSession(DateTime.UtcNow.AddHours(-1));

        await Create().ExecuteAsync("0002");

        Assert.Single(_db.Companies);
    }
}
