using SiagroB1.Application.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Permissões fixas por teste: quem for concedido tem; o resto não.</summary>
public class FakeUserPermissions(params string[] granted) : IUserPermissions
{
    public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(granted.Contains(permissionCode));

    public Task<List<string>> GetAsync(string username) => Task.FromResult(granted.ToList());

    public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(false);
}
