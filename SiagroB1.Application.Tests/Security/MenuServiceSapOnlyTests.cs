using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// GAC-1226: a tela "Integração SAP" só existe em SAPB1. O modelo de menu não tem marca "só SAP" (sem mudança de
/// esquema), então a regra fica no MenuService, ao lado do filtro StandaloneOnly.
/// </summary>
public class MenuServiceSapOnlyTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<List<string?>> ChildKeysAsync(string? erp)
    {
        var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "P1", CompanyCode = "0001" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "P1", RoleCode = "ADMIN" });
        db.MenuItems.AddRange(
            new MenuItem { Key = "registers", Title = "Cadastros", Order = 2 },
            new MenuItem { Key = "usages", Title = "Naturezas", Order = 13, ParentKey = "registers" },
            new MenuItem { Key = "sapIntegrationSettings", Title = "Integração SAP", Order = 20, ParentKey = "registers" });
        db.RolesMenus.AddRange(
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "registers" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "usages" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "sapIntegrationSettings" });
        await db.SaveChangesAsync();

        var menu = await new MenuService(db, TaxTestServices.Config(erp), new FakeCompanyContext("0001")).GetMenuAsync(UserId);
        return Assert.Single(menu.Navigation).Items!.Select(i => i.Key).Order().ToList();
    }

    [Fact]
    public async Task Sapb1_shows_the_sap_integration_item() =>
        Assert.Equal(["sapIntegrationSettings", "usages"], await ChildKeysAsync("SAPB1"));

    [Fact]
    public async Task Standalone_hides_the_sap_integration_item() =>
        Assert.Equal(["usages"], await ChildKeysAsync("STANDALONE"));

    [Fact]
    public async Task Missing_erp_key_hides_the_sap_integration_item() =>
        Assert.Equal(["usages"], await ChildKeysAsync(null));
}
