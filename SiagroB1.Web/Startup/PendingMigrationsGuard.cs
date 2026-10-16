using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Startup;

public enum PendingMigrationsOutcome
{
    Proceed,
    Warn,
    Block
}

public sealed record PendingMigrationsDecision(PendingMigrationsOutcome Outcome, string Message);

/// <summary>
/// Recusa subir o Web enquanto houver migration pendente no <see cref="CommonDbContext"/> ou no
/// <see cref="AppDbContext"/> de qualquer banco de negócio cadastrado (uma empresa por banco).
///
/// Migration é aplicada à mão no deploy. Quando alguém esquece, nada acusa: o sistema sobe e só a
/// tela que usa a tabela nova quebra, muitas vezes em silêncio - foi assim que o layout das tabelas
/// de todos os usuários se perdeu em 10/09/2026. Aplicar no startup (<c>Migrate()</c>) foi
/// descartado: Web, Gateway e Reports sobem juntos e concorreriam pelo DDL, o usuário de runtime
/// precisaria de permissão de DDL, e as migrations deste projeto carregam saneamento de dados escrito
/// à mão, que não deve rodar pela metade no boot.
///
/// Só o Web faz a checagem: é o único host que registra os contextos com o assembly de migrations,
/// e sem ele o sistema inteiro fica visivelmente fora (o Gateway devolve 502 no <c>/odata</c>).
///
/// Não atrapalha o <c>dotnet ef database update</c>: a ferramenta intercepta o host dentro do
/// <c>builder.Build()</c> e encerra antes de qualquer código posterior rodar.
/// </summary>
public static class PendingMigrationsGuard
{
    /// <summary>Escape de emergência: com <c>true</c>, a pendência vira só um aviso no log.</summary>
    public const string AllowPendingConfigurationKey = "Migrations:AllowPendingOnStartup";

    public static PendingMigrationsDecision Evaluate(
        IReadOnlyDictionary<string, IReadOnlyList<string>> pendingByContext,
        bool allowPending)
    {
        var withPending = pendingByContext
            .Where(entry => entry.Value.Count > 0)
            .Select(entry => $"{entry.Key}: {string.Join(", ", entry.Value)}")
            .ToList();

        if (withPending.Count == 0)
        {
            return new PendingMigrationsDecision(PendingMigrationsOutcome.Proceed, string.Empty);
        }

        var list = string.Join(" | ", withPending);

        if (allowPending)
        {
            return new PendingMigrationsDecision(
                PendingMigrationsOutcome.Warn,
                $"Migrations pendentes, e o boot seguiu porque {AllowPendingConfigurationKey}=true. " +
                $"Telas que dependem delas vão falhar. {list}");
        }

        return new PendingMigrationsDecision(
            PendingMigrationsOutcome.Block,
            "O SiagroB1.Web não vai subir: há migrations pendentes. Aplique-as com " +
            "`dotnet ef database update --context <Contexto>` apontando para o banco deste ambiente " +
            "(para o banco de uma empresa, acrescente --connection \"<string>\") " +
            $"(ou, só em emergência, configure {AllowPendingConfigurationKey}=true). {list}");
    }

    /// <summary>Só a COMMON — roda ANTES do bootstrap de empresas, que precisa das colunas novas dela.</summary>
    public static async Task EnsureNoPendingCommonMigrationsAsync(
        WebApplication app, CancellationToken cancellationToken = default)
    {
        var logger = CreateLogger(app);
        Dictionary<string, IReadOnlyList<string>> pending;

        try
        {
            using var scope = app.Services.CreateScope();
            pending = new Dictionary<string, IReadOnlyList<string>>
            {
                [nameof(CommonDbContext)] = await PendingOf<CommonDbContext>(scope, cancellationToken)
            };
        }
        catch (DbException ex)
        {
            logger.LogError(ex, "Não foi possível verificar as migrations pendentes da COMMON (banco inacessível). O boot segue.");
            return;
        }

        Decide(app, logger, pending);
    }

    /// <summary>
    /// O AppDbContext de CADA empresa cadastrada, mais o banco de ConnectionStrings:SiagroDB quando ele
    /// não está entre elas. Uma empresa inacessível só gera log — indisponibilidade não é o que esta
    /// trava protege.
    /// </summary>
    public static async Task EnsureNoPendingCompanyMigrationsAsync(
        WebApplication app, CancellationToken cancellationToken = default)
    {
        var logger = CreateLogger(app);
        var pending = new Dictionary<string, IReadOnlyList<string>>();

        List<CompanyInfo> targets;
        try
        {
            targets = [.. await app.Services.GetRequiredService<ICompanyCatalog>().GetAllAsync(cancellationToken)];
        }
        catch (DbException ex)
        {
            logger.LogError(ex, "Não foi possível ler as empresas para verificar as migrations (banco inacessível). O boot segue.");
            return;
        }

        var fallback = app.Configuration.GetConnectionString(CompanyContext.FallbackConnectionStringName);
        if (fallback is not null && !targets.Any(t => SameDatabase(t.ConnectionString, fallback)))
        {
            targets.Add(new CompanyInfo("CFG", "ConnectionStrings:SiagroDB", fallback));
        }

        foreach (var company in targets)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
                pending[$"{nameof(AppDbContext)}[{company.Code}]"] = await PendingOf<AppDbContext>(scope, cancellationToken);
            }
            catch (DbException ex)
            {
                logger.LogError(ex, "Banco da empresa {Code} inacessível; o boot segue sem verificar as migrations dela.", company.Code);
            }
        }

        Decide(app, logger, pending);
    }

    private static void Decide(WebApplication app, ILogger logger, Dictionary<string, IReadOnlyList<string>> pending)
    {
        var decision = Evaluate(pending, app.Configuration.GetValue<bool>(AllowPendingConfigurationKey));

        switch (decision.Outcome)
        {
            case PendingMigrationsOutcome.Block:
                logger.LogCritical("{Message}", decision.Message);
                throw new InvalidOperationException(decision.Message);

            case PendingMigrationsOutcome.Warn:
                logger.LogWarning("{Message}", decision.Message);
                break;

            default:
                logger.LogInformation("Nenhuma migration pendente em {Contexts}.", string.Join(", ", pending.Keys));
                break;
        }
    }

    private static ILogger CreateLogger(WebApplication app) =>
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(PendingMigrationsGuard));

    /// <summary>Mesmo servidor e mesmo catálogo; a comparação textual falharia por diferença de ordem ou caixa.</summary>
    private static bool SameDatabase(string a, string b)
    {
        try
        {
            var x = new SqlConnectionStringBuilder(a);
            var y = new SqlConnectionStringBuilder(b);
            return string.Equals(x.DataSource, y.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.InitialCatalog, y.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<IReadOnlyList<string>> PendingOf<TContext>(
        IServiceScope scope, CancellationToken cancellationToken) where TContext : DbContext
    {
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        return (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
    }
}
