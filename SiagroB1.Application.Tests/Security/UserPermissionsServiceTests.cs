using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Security;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Security;

public class UserPermissionsServiceTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>usuário -> perfil -> papel -> permissão, que é o caminho real do cadastro.</summary>
    private static void GrantPermission(CommonDbContext db, string username, string permissionCode,
        bool isAdmin = false)
    {
        var user = new User { Username = username, FullName = username, IsAdmin = isAdmin };
        db.Users.Add(user);

        db.Permissions.Add(new Permission { Code = permissionCode, Description = permissionCode });
        db.Roles.Add(new Role { Code = "OPERADOR" });
        db.Profiles.Add(new Profile { Code = "BALANCA", Description = "Balança" });
        db.RolesPermissions.Add(new RolePermission { RoleCode = "OPERADOR", PermissionCode = permissionCode });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "BALANCA", RoleCode = "OPERADOR" });
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "BALANCA", CompanyCode = "0001" });

        db.SaveChanges();
    }

    [Fact]
    public async Task Returns_the_permission_granted_through_profile_and_role()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.True(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], await service.GetAsync("joao"));
    }

    [Fact]
    public async Task Returns_false_for_a_permission_that_was_not_granted()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "SOME_OTHER_PERMISSION");

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.False(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_admin_has_every_permission()
    {
        using var db = CreateDb();
        GrantPermission(db, "admin", "SOME_OTHER_PERMISSION", isAdmin: true);

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.True(await service.HasAsync("admin", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_unknown_user_has_no_permission()
    {
        using var db = CreateDb();

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.False(await service.HasAsync("ninguem", "WEIGHING_MANUAL_ENTRY"));
        Assert.Empty(await service.GetAsync("ninguem"));
    }

    [Fact]
    public async Task Duplicated_grants_are_returned_once()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        db.Roles.Add(new Role { Code = "SUPERVISOR" });
        db.RolesPermissions.Add(new RolePermission
        {
            RoleCode = "SUPERVISOR",
            PermissionCode = "WEIGHING_MANUAL_ENTRY"
        });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "BALANCA", RoleCode = "SUPERVISOR" });
        await db.SaveChangesAsync();

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.Single(await service.GetAsync("joao"));
    }

    [Fact]
    public async Task HasRoleAsync_is_true_for_the_role_granted_through_profile()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.True(await service.HasRoleAsync("joao", "OPERADOR"));
    }

    [Fact]
    public async Task HasRoleAsync_does_not_bypass_for_IsAdmin()
    {
        using var db = CreateDb();
        GrantPermission(db, "admin", "SOME_OTHER_PERMISSION", isAdmin: true);

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.False(await service.HasRoleAsync("admin", "ADMIN"));
    }

    [Fact]
    public async Task HasRoleAsync_is_false_for_a_user_without_that_role()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.False(await service.HasRoleAsync("joao", "ADMIN"));
    }

    [Fact]
    public async Task HasRoleAsync_is_false_for_an_unknown_user()
    {
        using var db = CreateDb();

        var service = new UserPermissionsService(db, new FakeCompanyContext("0001"));

        Assert.False(await service.HasRoleAsync("ninguem", "ADMIN"));
    }

    [Fact]
    public async Task A_profile_in_another_company_does_not_leak_into_this_session()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");                 // perfil na 0001
        db.Roles.Add(new Role { Code = "SUPERVISOR" });
        db.RolesPermissions.Add(new RolePermission { RoleCode = "SUPERVISOR", PermissionCode = "OTHER_COMPANY_ONLY" });
        db.Profiles.Add(new Profile { Code = "SUP", Description = "Supervisor" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "SUP", RoleCode = "SUPERVISOR" });
        var userId = db.Users.Single(u => u.Username == "joao").Id;
        db.UserProfiles.Add(new UserProfile { UserId = userId, ProfileCode = "SUP", CompanyCode = "0002" });
        await db.SaveChangesAsync();

        var inCompany1 = new UserPermissionsService(db, new FakeCompanyContext("0001"));
        var inCompany2 = new UserPermissionsService(db, new FakeCompanyContext("0002"));

        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], await inCompany1.GetAsync("joao"));
        Assert.False(await inCompany1.HasRoleAsync("joao", "SUPERVISOR"));
        Assert.Equal(["OTHER_COMPANY_ONLY"], await inCompany2.GetAsync("joao"));
        Assert.True(await inCompany2.HasRoleAsync("joao", "SUPERVISOR"));
    }
}
