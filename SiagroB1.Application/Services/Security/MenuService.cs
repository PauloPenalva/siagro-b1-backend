using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Security;

public class MenuService(CommonDbContext context)
{
    public async Task<NavigationResponseDto> GetMenuAsync(Guid userId)
    {
        var query =
            from up in context.UserProfiles

            join pr in context.ProfileRoles
                on up.ProfileCode equals pr.ProfileCode

            join rm in context.RolesMenus
                on pr.RoleCode equals rm.RoleCode

            join menu in context.MenuItems
                on rm.MenuItemKey equals menu.Key

            where up.UserId == userId

            select new
            {
                Menu = menu,
                RoleCode = pr.RoleCode
            };

        var data = await query
            .Distinct()
            .ToListAsync();

        var menuIds = data
            .Select(x => x.Menu.Key)
            .Distinct()
            .ToList();

        var permissions =
            await (
                from rp in context.RolesPermissions

                join p in context.Permissions
                    on rp.PermissionCode equals p.Code

                where data.Select(x => x.RoleCode)
                    .Contains(rp.RoleCode)

                select new MenuPermissionDto()
                {
                    RoleCode = rp.RoleCode,
                    PermissionCode = p.Code
                }
            ).ToListAsync();

        var menus = data
            .Select(x => x.Menu)
            .DistinctBy(x => x.Key)
            .ToList();

        var lookup = menus.ToDictionary(x => x.Key);

        var roots = new List<Domain.Entities.Common.MenuItem>();

        foreach (var item in menus)
        {
            if (item.ParentKey == null)
            {
                roots.Add(item);
                continue;
            }

            if (lookup.TryGetValue(item.ParentKey,
                out var parent))
            {
                parent.Children.Add(item);
            }
        }

        return new NavigationResponseDto
        {
            Navigation = roots
                .OrderBy(x => x.Order)
                .Select(x => Map(x, permissions))
                .ToList()
        };
    }

    private NavigationItemDto Map(
        Domain.Entities.Common.MenuItem item,
        List<MenuPermissionDto> permissions)
    {
        return new NavigationItemDto
        {
            Title = item.Title,
            Key = item.Key,
            Enabled = item.Enabled,
            Expanded = item.Expanded,
            Icon = item.Icon,

            Permissions = permissions
                .Select(x => (string)x.PermissionCode)
                .Distinct()
                .ToList(),

            Items = item.Children
                .OrderBy(x => x.Order)
                .Select(x => Map(x, permissions))
                .ToList()
        };
    }
}