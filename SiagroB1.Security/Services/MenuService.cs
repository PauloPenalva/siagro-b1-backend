using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Services;

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
            .Select(x => new MenuNode
            {
                Key = x.Menu.Key,
                ParentKey = x.Menu.ParentKey,
                Title = x.Menu.Title,
                Icon = x.Menu.Icon,
                Enabled = x.Menu.Enabled,
                Expanded = x.Menu.Expanded,
                Order = x.Menu.Order
            })
            .DistinctBy(x => x.Key)
            .ToList();
        
        var lookup = menus.ToDictionary(x => x.Key);

        var roots = new List<MenuNode>();

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
        MenuNode item,
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
    
    private class MenuNode
    {
        public string Key { get; set; } = "";

        public string? ParentKey { get; set; }

        public string Title { get; set; } = "";

        public string Icon { get; set; } = "";

        public bool Enabled { get; set; }

        public bool Expanded { get; set; }

        public int Order { get; set; }

        public List<MenuNode> Children { get; } = [];
    }
}