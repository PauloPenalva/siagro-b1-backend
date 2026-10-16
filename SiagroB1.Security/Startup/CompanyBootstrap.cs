using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Startup;

public sealed record CompanyBootstrapResult(
    bool CreatedDefault, string DefaultCode, int ProfilesAssigned, int SessionsAssigned, string? Warning);

/// <summary>
/// Faz um deploy existente virar uma instalação de UMA empresa sem passo manual: com COMPANIES
/// vazia, cadastra a empresa padrão a partir da configuração (<c>Company:Code</c>, <c>Company:Name</c>
/// ou <c>CompanyName</c>, <c>ConnectionStrings:SiagroDB</c>) e atribui a ela os perfis e as sessões
/// ativas que ainda não têm empresa (linhas anteriores à migration).
///
/// Idempotente e tolerante a corrida: Web e Gateway sobem juntos e os dois rodam isto.
/// Sem <c>ExecuteUpdateAsync</c> de propósito: o provider InMemory dos testes não o suporta e as
/// linhas são poucas.
/// </summary>
public static class CompanyBootstrap
{
    public const string CodeKey = "Company:Code";
    public const string NameKey = "Company:Name";

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CompanyBootstrap));

        try
        {
            var result = await RunAsync(
                scope.ServiceProvider.GetRequiredService<CommonDbContext>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                ct);

            if (result.CreatedDefault)
                logger.LogInformation("Empresa padrão {Code} cadastrada a partir da configuração.", result.DefaultCode);

            if (result.ProfilesAssigned > 0 || result.SessionsAssigned > 0)
                logger.LogInformation("{Profiles} perfil(is) e {Sessions} sessão(ões) sem empresa atribuídos à {Code}.",
                    result.ProfilesAssigned, result.SessionsAssigned, result.DefaultCode);

            if (result.Warning is not null)
                logger.LogWarning("{Warning}", result.Warning);

            scope.ServiceProvider.GetRequiredService<ICompanyCatalog>().Invalidate();
        }
        catch (DbException ex)
        {
            // Mesma postura do guard de migrations: banco fora do ar no boot não derruba o host.
            logger.LogError(ex, "Bootstrap de empresas não executado (banco COMMON inacessível ou migration pendente). O boot segue.");
        }
    }

    public static async Task<CompanyBootstrapResult> RunAsync(
        CommonDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        var configuredCode = (configuration[CodeKey] ?? CompanyCatalog.FallbackDefaultCode).Trim().ToUpperInvariant();
        var created = false;

        if (!await db.Companies.AnyAsync(ct))
        {
            var connectionString = configuration.GetConnectionString(CompanyContext.FallbackConnectionStringName)
                ?? throw new InvalidOperationException("ConnectionStrings:SiagroDB ausente: não há como cadastrar a empresa padrão.");

            db.Companies.Add(new Company
            {
                Code = configuredCode,
                CompanyName = configuration[NameKey] ?? configuration["CompanyName"] ?? configuredCode,
                ConnectionString = connectionString,
            });

            try
            {
                await db.SaveChangesAsync(ct);
                created = true;
            }
            catch (DbUpdateException)
            {
                // Web e Gateway correram; o outro venceu. Segue com o que está no banco.
                db.ChangeTracker.Clear();
                if (!await db.Companies.AnyAsync(ct)) throw;
            }
        }

        var codes = await db.Companies.Select(c => c.Code).ToListAsync(ct);
        string? warning = null;
        var defaultCode = codes.FirstOrDefault(c => string.Equals(c, configuredCode, StringComparison.OrdinalIgnoreCase));

        if (defaultCode is null)
        {
            defaultCode = codes.Order(StringComparer.Ordinal).First();
            warning = $"{CodeKey}={configuredCode} não existe em COMPANIES; perfis e sessões sem empresa foram atribuídos à {defaultCode}.";
        }

        var profiles = await db.UserProfiles.Where(p => p.CompanyCode == null).ToListAsync(ct);
        foreach (var profile in profiles) profile.CompanyCode = defaultCode;

        var sessions = await db.UserSessions.Where(s => s.CompanyCode == null && s.IsActive).ToListAsync(ct);
        foreach (var session in sessions) session.CompanyCode = defaultCode;

        if (profiles.Count > 0 || sessions.Count > 0)
            await db.SaveChangesAsync(ct);

        return new CompanyBootstrapResult(created, defaultCode, profiles.Count, sessions.Count, warning);
    }
}
