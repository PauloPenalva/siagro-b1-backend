using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Services.SapUsers;

/// <summary>
/// Traz do OUSR apenas o usuário que está tentando entrar (ou pedindo a senha).
///
/// Nunca lança: o SAP fora do ar não pode derrubar o login de quem já existe no SiagroB1. A falha
/// vira um warning e o fluxo segue com o cadastro local.
/// </summary>
public class SapUserProvisioner(
    CommonDbContext db,
    SapErpDbContext sapDb,
    ILogger<SapUserProvisioner> logger) : ISapUserProvisioner
{
    public async Task EnsureAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            return;
        }

        var identifier = usernameOrEmail.Trim();

        try
        {
            var sapUser = await sapDb.SapUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserCode == identifier || u.Email == identifier, ct);

            if (sapUser is null)
            {
                return;
            }

            var username = SapUserMapper.Username(sapUser);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
            var currentId = user?.Id ?? Guid.Empty;

            bool IsEmailTakenByAnotherUser(string email) =>
                db.Users.Any(u => u.Email == email && u.Id != currentId);

            if (user is null)
            {
                db.Users.Add(SapUserMapper.CreateFrom(sapUser, IsEmailTakenByAnotherUser));
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Usuário {Username} criado a partir do OUSR.", username);
                return;
            }

            if (SapUserMapper.Apply(sapUser, user, IsEmailTakenByAnotherUser))
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Usuário {Username} atualizado a partir do OUSR.", username);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Falha ao consultar o cadastro de usuários do SAP. Seguindo com o cadastro local.");
        }
    }
}
