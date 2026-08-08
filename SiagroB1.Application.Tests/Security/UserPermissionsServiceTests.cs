using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Security;
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
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "BALANCA" });

        db.SaveChanges();
    }

    [Fact]
    public async Task Returns_the_permission_granted_through_profile_and_role()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        var service = new UserPermissionsService(db);

        Assert.True(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], await service.GetAsync("joao"));
    }

    [Fact]
    public async Task Returns_false_for_a_permission_that_was_not_granted()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "SOME_OTHER_PERMISSION");

        var service = new UserPermissionsService(db);

        Assert.False(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_admin_has_every_permission()
    {
        using var db = CreateDb();
        GrantPermission(db, "admin", "SOME_OTHER_PERMISSION", isAdmin: true);

        var service = new UserPermissionsService(db);

        Assert.True(await service.HasAsync("admin", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_unknown_user_has_no_permission()
    {
        using var db = CreateDb();

        var service = new UserPermissionsService(db);

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

        var service = new UserPermissionsService(db);

        Assert.Single(await service.GetAsync("joao"));
    }
}
