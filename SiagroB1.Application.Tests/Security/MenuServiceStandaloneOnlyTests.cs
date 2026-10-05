using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Telas que só existem no STANDALONE (condições de pagamento, configuração da NF-e) somem do
/// menu nos outros modos. Quem monta o menu é o Gateway, que conhece o Erp.
/// </summary>
public class MenuServiceStandaloneOnlyTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<CommonDbContext> SeedAsync()
    {
        var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "P1" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "P1", RoleCode = "ADMIN" });
        db.MenuItems.AddRange(
            new MenuItem { Key = "registers", Title = "Cadastros", Order = 2 },
            new MenuItem { Key = "usages", Title = "Naturezas", Order = 13, ParentKey = "registers" },
            new MenuItem { Key = "paymentConditions", Title = "Condições de Pagamento", Order = 16, ParentKey = "registers", StandaloneOnly = true });
        db.RolesMenus.AddRange(
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "registers" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "usages" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "paymentConditions" });
        await db.SaveChangesAsync();

        return db;
    }

    private static async Task<List<string?>> ChildKeysAsync(string? erp)
    {
        var db = await SeedAsync();
        var menu = await new MenuService(db, TaxTestServices.Config(erp)).GetMenuAsync(UserId);

        return Assert.Single(menu.Navigation).Items!.Select(i => i.Key).Order().ToList();
    }

    [Fact]
    public async Task Standalone_shows_standalone_only_items() =>
        Assert.Equal(["paymentConditions", "usages"], await ChildKeysAsync("STANDALONE"));

    [Fact]
    public async Task Missing_erp_key_counts_as_standalone() =>
        Assert.Contains("paymentConditions", await ChildKeysAsync(null));

    [Fact]
    public async Task Sapb1_hides_standalone_only_items() =>
        Assert.Equal(["usages"], await ChildKeysAsync("SAPB1"));
}
