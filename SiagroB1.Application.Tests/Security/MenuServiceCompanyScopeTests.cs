using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

/// <summary>O menu da sessão vem só dos perfis da empresa em que o usuário entrou.</summary>
public class MenuServiceCompanyScopeTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<CommonDbContext> SeedAsync()
    {
        var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "COMPRAS", CompanyCode = "0001" });
        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "ADMIN", CompanyCode = "0002" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "COMPRAS", RoleCode = "COMPRADOR" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "ADMIN", RoleCode = "ADMIN" });
        db.MenuItems.AddRange(
            new MenuItem { Key = "purchases", Title = "Compras", Order = 1 },
            new MenuItem { Key = "admin", Title = "Administração", Order = 2 });
        db.RolesMenus.AddRange(
            new RoleMenu { RoleCode = "COMPRADOR", MenuItemKey = "purchases" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "admin" });
        await db.SaveChangesAsync();
        return db;
    }

    [Theory]
    [InlineData("0001", "purchases")]
    [InlineData("0002", "admin")]
    public async Task Menu_contains_only_the_items_of_the_session_company(string company, string expectedRoot)
    {
        var db = await SeedAsync();

        var menu = await new MenuService(db, TaxTestServices.Config("STANDALONE"), new FakeCompanyContext(company))
            .GetMenuAsync(UserId);

        Assert.Equal(expectedRoot, Assert.Single(menu.Navigation).Key);
    }

    [Fact]
    public async Task Admin_sees_the_menu_of_all_their_profiles_regardless_of_session_company()
    {
        var db = await SeedAsync();
        db.Users.Add(new User { Id = UserId, Username = "admin", FullName = "admin", IsAdmin = true });
        await db.SaveChangesAsync();

        var menu = await new MenuService(db, TaxTestServices.Config("STANDALONE"), new FakeCompanyContext("0002"))
            .GetMenuAsync(UserId);

        Assert.Contains(menu.Navigation, n => n.Key == "purchases");
    }
}
