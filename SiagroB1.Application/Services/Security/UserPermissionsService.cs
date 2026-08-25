using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Security;

/// <summary>
/// Permissões efetivas de um usuário: usuário -> perfis -> papéis -> permissões. Administrador
/// passa por cima de tudo, como já acontece no resto do sistema.
/// </summary>
public class UserPermissionsService(CommonDbContext db) : IUserPermissions
{
    public async Task<bool> HasAsync(string username, string permissionCode)
    {
        if (await IsAdminAsync(username))
            return true;

        var permissions = await GetAsync(username);

        return permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> GetAsync(string username)
    {
        var query =
            from u in db.Users
            join up in db.UserProfiles on u.Id equals up.UserId
            join pr in db.ProfileRoles on up.ProfileCode equals pr.ProfileCode
            join rp in db.RolesPermissions on pr.RoleCode equals rp.RoleCode
            where u.Username == username && u.IsActive
            select rp.PermissionCode;

        return await query.Distinct().ToListAsync();
    }

    private async Task<bool> IsAdminAsync(string username) =>
        await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Username == username && u.IsActive && u.IsAdmin);

    /// <summary>Papel atribuído via perfil, sem o bypass de <c>IsAdmin</c> — para checar o papel em si.</summary>
    public async Task<bool> HasRoleAsync(string username, string roleCode)
    {
        var query =
            from u in db.Users
            join up in db.UserProfiles on u.Id equals up.UserId
            join pr in db.ProfileRoles on up.ProfileCode equals pr.ProfileCode
            where u.Username == username && u.IsActive && pr.RoleCode == roleCode
            select pr;

        return await query.AnyAsync();
    }
}
