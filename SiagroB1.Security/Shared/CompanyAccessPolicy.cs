using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Shared;

/// <summary>
/// Quem pode entrar em qual empresa: administrador em todas; os demais, onde têm ao menos um
/// perfil atribuído. Derivado dos perfis de propósito — uma tabela de acesso à parte ficaria
/// dessincronizada, e um usuário sem perfil na empresa não faria nada nela mesmo.
/// </summary>
public static class CompanyAccessPolicy
{
    public static Task<bool> CanLoginAsync(CommonDbContext db, User user, string companyCode, CancellationToken ct = default) =>
        user.IsAdmin
            ? Task.FromResult(true)
            : db.UserProfiles.AnyAsync(up => up.UserId == user.Id && up.CompanyCode == companyCode, ct);

    public static string NoAccessMessage(string companyName) => $"Usuário sem acesso à empresa {companyName}.";
}
