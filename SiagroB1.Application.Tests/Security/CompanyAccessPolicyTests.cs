using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>Entra na empresa quem é admin ou tem ao menos um perfil nela. Não há tabela de acesso à parte.</summary>
public class CompanyAccessPolicyTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task User_with_a_profile_in_the_company_can_login_there_and_nowhere_else()
    {
        using var db = CreateDb();
        var user = new User { Username = "joao", FullName = "João" };
        db.Users.Add(user);
        db.Profiles.Add(new Profile { Code = "OPER", Description = "Operador" });
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "OPER", CompanyCode = "0002" });
        await db.SaveChangesAsync();

        Assert.True(await CompanyAccessPolicy.CanLoginAsync(db, user, "0002"));
        Assert.False(await CompanyAccessPolicy.CanLoginAsync(db, user, "0001"));
    }

    [Fact]
    public async Task Admin_can_login_anywhere_even_without_profiles()
    {
        using var db = CreateDb();
        var admin = new User { Username = "admin", FullName = "Admin", IsAdmin = true };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        Assert.True(await CompanyAccessPolicy.CanLoginAsync(db, admin, "0002"));
    }

    [Fact]
    public void Message_names_the_company()
    {
        Assert.Equal("Usuário sem acesso à empresa LM CEREAIS.", CompanyAccessPolicy.NoAccessMessage("LM CEREAIS"));
    }
}
