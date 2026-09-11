using System.Data.Common;
using Microsoft.EntityFrameworkCore;
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
/// Recusa subir o Web enquanto houver migration pendente no <see cref="AppDbContext"/> ou no
/// <see cref="CommonDbContext"/>.
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
            $"(ou, só em emergência, configure {AllowPendingConfigurationKey}=true). {list}");
    }

    public static async Task EnsureNoPendingMigrationsAsync(
        WebApplication app, CancellationToken cancellationToken = default)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(PendingMigrationsGuard));

        Dictionary<string, IReadOnlyList<string>> pending;

        try
        {
            using var scope = app.Services.CreateScope();

            pending = new Dictionary<string, IReadOnlyList<string>>
            {
                [nameof(AppDbContext)] = await PendingOf<AppDbContext>(scope, cancellationToken),
                [nameof(CommonDbContext)] = await PendingOf<CommonDbContext>(scope, cancellationToken)
            };
        }
        catch (DbException ex)
        {
            // Banco fora do ar no boot não é o que esta trava protege: sem a checagem o Web sobe como
            // sempre subiu, e uma queda momentânea do SQL Server não o deixa parado até alguém
            // reiniciar o serviço.
            logger.LogError(ex,
                "Não foi possível verificar as migrations pendentes (banco inacessível). O boot segue.");
            return;
        }

        var decision = Evaluate(
            pending, app.Configuration.GetValue<bool>(AllowPendingConfigurationKey));

        switch (decision.Outcome)
        {
            case PendingMigrationsOutcome.Block:
                logger.LogCritical("{Message}", decision.Message);
                throw new InvalidOperationException(decision.Message);

            case PendingMigrationsOutcome.Warn:
                logger.LogWarning("{Message}", decision.Message);
                break;

            default:
                logger.LogInformation("Nenhuma migration pendente.");
                break;
        }
    }

    private static async Task<IReadOnlyList<string>> PendingOf<TContext>(
        IServiceScope scope, CancellationToken cancellationToken) where TContext : DbContext
    {
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        return (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
    }
}
