using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Market;

/// <summary>Ponto único de checagem das permissões de gravação do Mercado (respeita o flag IsAdmin do usuário).</summary>
public class MarketPermissionGuard(IUserPermissions permissions)
{
    public const string DeniedMessage = "Você não tem permissão para esta operação de Trading.";

    public async Task EnsureAsync(string userName, string permissionCode)
    {
        if (!await permissions.HasAsync(userName, permissionCode))
            throw new ForbiddenException(DeniedMessage);
    }
}
