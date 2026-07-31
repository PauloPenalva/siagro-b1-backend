using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services.SapUsers;

namespace SiagroB1.Application.Services.Users;

/// <param name="EmailsDiscarded">
/// E-mails do SAP que não puderam ser gravados (vazios, longos demais ou repetidos).
/// USERS.Email tem índice único: gravá-los derrubaria a sincronização inteira.
/// </param>
/// <param name="Skipped">
/// Usuários do SAP ignorados por terem o mesmo nome de outro já processado nesta rodada.
/// </param>
public record SapUserSyncResult(int Created, int Updated, int Deactivated, int EmailsDiscarded, int Skipped);

/// <summary>
/// Varredura completa do cadastro de usuários do SAP (OUSR) sobre a tabela USERS.
///
/// É o caminho que enxerga as ausências: um usuário que sumiu do OUSR só pode ser detectado
/// olhando o cadastro inteiro, e por isso a desativação acontece aqui e não no provisionamento
/// pontual do login.
///
/// Ninguém é apagado, nunca — as linhas de USERS respondem por autoria e histórico em todo o
/// sistema. Some-se do SAP e o usuário apenas fica inativo.
///
/// Toda comparação de nome e e-mail usa <see cref="SapUserMapper.NormalizeKey"/>, que reproduz a
/// collation <c>CI_AI</c> dos índices únicos: sem isso, dois usuários do SAP que difiram só por
/// acento ("João" e "Joao", ambos presentes em cadastros reais) derrubam a gravação inteira.
/// </summary>
public class SapUserSyncService(
    CommonDbContext db,
    SapErpDbContext sapDb,
    IConfiguration configuration,
    ILogger<SapUserSyncService> logger)
{
    /// <summary>
    /// Usuários locais que a varredura nunca desativa.
    ///
    /// Sem isto, a primeira execução desativaria o <c>admin</c> — que não existe no SAP — e
    /// trancaria todo mundo para fora do sistema.
    /// </summary>
    private static readonly string[] DefaultProtectedUsernames = ["admin"];

    public async Task<SapUserSyncResult> ExecuteAsync(CancellationToken ct = default)
    {
        var sapUsers = await sapDb.SapUsers.AsNoTracking().ToListAsync(ct);
        var localUsers = await db.Users.ToListAsync(ct);

        // Índices por chave normalizada, espelhando os índices únicos do banco.
        var usersByName = new Dictionary<string, User>();
        var emailOwners = new Dictionary<string, User>();

        foreach (var user in localUsers)
        {
            usersByName.TryAdd(SapUserMapper.NormalizeKey(user.Username), user);

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                emailOwners.TryAdd(SapUserMapper.NormalizeKey(user.Email), user);
            }
        }

        var handledNames = new HashSet<string>();
        var created = 0;
        var updated = 0;
        var emailsDiscarded = 0;
        var skipped = 0;

        foreach (var sapUser in sapUsers)
        {
            var username = SapUserMapper.Username(sapUser);
            var nameKey = SapUserMapper.NormalizeKey(username);

            if (nameKey.Length == 0)
            {
                continue;
            }

            // Dois usuários do SAP com nomes que o banco considera iguais: o primeiro fica, o
            // segundo é ignorado. Tentar gravar os dois violaria o índice único e abortaria a
            // sincronização inteira, deixando o cadastro sem nenhuma atualização.
            if (!handledNames.Add(nameKey))
            {
                skipped++;
                logger.LogWarning(
                    "Usuário {Username} do SAP ignorado: outro usuário com o mesmo nome " +
                    "(desconsiderando maiúsculas e acentos) já foi sincronizado.", username);
                continue;
            }

            usersByName.TryGetValue(nameKey, out var user);

            bool IsEmailTakenByAnother(string email) =>
                emailOwners.TryGetValue(SapUserMapper.NormalizeKey(email), out var owner) &&
                !ReferenceEquals(owner, user);

            if (user is null)
            {
                user = SapUserMapper.CreateFrom(sapUser, IsEmailTakenByAnother);
                db.Users.Add(user);
                usersByName[nameKey] = user;
                localUsers.Add(user);
                created++;
            }
            else if (SapUserMapper.Apply(sapUser, user, IsEmailTakenByAnother))
            {
                updated++;
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                emailOwners.TryAdd(SapUserMapper.NormalizeKey(user.Email), user);
            }

            if (!string.IsNullOrWhiteSpace(sapUser.Email) && string.IsNullOrEmpty(user.Email))
            {
                emailsDiscarded++;
                logger.LogWarning(
                    "E-mail do usuário {Username} não foi gravado: vazio, longo demais ou já " +
                    "usado por outro usuário.", username);
            }
        }

        var deactivated = DeactivateUsersMissingFromSap(handledNames, localUsers);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Sincronização de usuários do SAP: {Created} criados, {Updated} atualizados, " +
            "{Deactivated} desativados, {EmailsDiscarded} e-mails descartados, {Skipped} ignorados.",
            created, updated, deactivated, emailsDiscarded, skipped);

        return new SapUserSyncResult(created, updated, deactivated, emailsDiscarded, skipped);
    }

    private int DeactivateUsersMissingFromSap(HashSet<string> sapUsernames, List<User> localUsers)
    {
        var protectedUsernames = ProtectedUsernames();
        var deactivated = 0;

        foreach (var user in localUsers.Where(u => u.IsActive))
        {
            var nameKey = SapUserMapper.NormalizeKey(user.Username);

            if (sapUsernames.Contains(nameKey) || protectedUsernames.Contains(nameKey))
            {
                continue;
            }

            user.IsActive = false;
            deactivated++;

            logger.LogInformation("Usuário {Username} desativado: não existe mais no OUSR.", user.Username);
        }

        return deactivated;
    }

    private HashSet<string> ProtectedUsernames()
    {
        var configured = configuration
            .GetSection("SapUserSync:ProtectedUsernames")
            .Get<string[]>();

        return (configured is { Length: > 0 } ? configured : DefaultProtectedUsernames)
            .Select(SapUserMapper.NormalizeKey)
            .ToHashSet();
    }
}
