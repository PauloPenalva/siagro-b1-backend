using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>Alterar os parâmetros da empresa é restrito ao papel ADMIN.</summary>
public class CompanySettingsControllerTests
{
    private sealed class RolePermissions(bool admin) : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(false);
        public Task<List<string>> GetAsync(string username) => Task.FromResult(new List<string>());
        public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(admin && roleCode == "ADMIN");
    }

    private static Delta<CompanySettings> Patch()
    {
        var patch = new Delta<CompanySettings>();
        patch.TrySetPropertyValue(nameof(CompanySettings.RequireCostCenter), true);
        return patch;
    }

    [Fact]
    public async Task Non_admin_patch_is_forbidden_and_changes_nothing()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = new CompanySettingsController(new CompanySettingsService(db), new RolePermissions(false))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Patch(1, Patch());

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        Assert.Equal("Somente administradores podem alterar os parâmetros da empresa.", status.Value);
        Assert.False((await new CompanySettingsService(db).GetAsync()).RequireCostCenter);
    }

    [Fact]
    public async Task Admin_patch_is_applied()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = new CompanySettingsController(new CompanySettingsService(db), new RolePermissions(true))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Patch(1, Patch());

        Assert.IsType<NoContentResult>(result);
        Assert.True((await new CompanySettingsService(db).GetAsync()).RequireCostCenter);
    }
}
