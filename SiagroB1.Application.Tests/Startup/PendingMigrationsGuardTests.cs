using SiagroB1.Web.Startup;

namespace SiagroB1.Application.Tests.Startup;

/// <summary>
/// Migration é aplicada à mão no deploy. Quando alguém esquece, o sistema sobe como se nada
/// tivesse acontecido e só a tela que usa a tabela nova quebra - em 10/09/2026 isso apagou o layout
/// das tabelas de todos os usuários (GAC-1163). A trava troca essa falha silenciosa por uma recusa
/// barulhenta no boot do Web.
/// </summary>
public class PendingMigrationsGuardTests
{
    private static Dictionary<string, IReadOnlyList<string>> Pending(
        params (string Context, string[] Migrations)[] contexts) =>
        contexts.ToDictionary(c => c.Context, c => (IReadOnlyList<string>)c.Migrations);

    [Fact]
    public void Evaluate_WithoutPendingMigrations_Proceeds()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext", []), ("CommonDbContext", [])), allowPending: false);

        Assert.Equal(PendingMigrationsOutcome.Proceed, decision.Outcome);
    }

    [Fact]
    public void Evaluate_WithPendingMigration_Blocks()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext", []),
                ("CommonDbContext", ["20260907173009_CreateTableUserTableLayouts"])),
            allowPending: false);

        Assert.Equal(PendingMigrationsOutcome.Block, decision.Outcome);
    }

    [Fact]
    public void Evaluate_WithPendingMigrations_MessageNamesEveryContextAndMigration()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext", ["20260901000000_A", "20260902000000_B"]),
                ("CommonDbContext", ["20260907173009_CreateTableUserTableLayouts"])),
            allowPending: false);

        Assert.Contains("AppDbContext", decision.Message);
        Assert.Contains("20260901000000_A", decision.Message);
        Assert.Contains("20260902000000_B", decision.Message);
        Assert.Contains("CommonDbContext", decision.Message);
        Assert.Contains("20260907173009_CreateTableUserTableLayouts", decision.Message);
    }

    [Fact]
    public void Evaluate_ContextWithoutPending_IsLeftOutOfTheMessage()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext", []),
                ("CommonDbContext", ["20260907173009_CreateTableUserTableLayouts"])),
            allowPending: false);

        Assert.DoesNotContain("AppDbContext", decision.Message);
    }

    [Fact]
    public void Evaluate_WithPendingAndEscapeFlag_OnlyWarns()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("CommonDbContext", ["20260907173009_CreateTableUserTableLayouts"])),
            allowPending: true);

        Assert.Equal(PendingMigrationsOutcome.Warn, decision.Outcome);
        Assert.Contains("20260907173009_CreateTableUserTableLayouts", decision.Message);
    }

    [Fact]
    public void Evaluate_WithoutPendingAndEscapeFlag_Proceeds()
    {
        var decision = PendingMigrationsGuard.Evaluate(
            Pending(("AppDbContext", []), ("CommonDbContext", [])), allowPending: true);

        Assert.Equal(PendingMigrationsOutcome.Proceed, decision.Outcome);
    }
}
