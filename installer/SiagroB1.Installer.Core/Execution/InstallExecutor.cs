namespace SiagroB1.Installer.Core.Execution;

public sealed record InstallOutcome(bool Success, string? FailedStep, string? Error, IReadOnlyList<string> RolledBack);

public static class InstallExecutor
{
    /// <summary>
    /// Executa os passos em ordem. Se um falhar, desfaz ele e os anteriores em ordem inversa;
    /// erro num rollback é registrado e não impede os demais.
    /// </summary>
    public static async Task<InstallOutcome> RunAsync(IReadOnlyList<IInstallStep> steps, InstallContext ctx, CancellationToken ct)
    {
        var done = new List<IInstallStep>();
        foreach (var step in steps)
        {
            ctx.Log.Report($"▶ {step.Title}");
            try
            {
                await step.ExecuteAsync(ctx, ct);
                done.Add(step);
            }
            catch (Exception ex)
            {
                ctx.Log.Report($"✖ {step.Title}: {ex.Message}");
                done.Add(step);
                var rolledBack = await RollbackAsync(done, ctx);
                return new InstallOutcome(false, step.Title, ex.Message, rolledBack);
            }
        }
        return new InstallOutcome(true, null, null, []);
    }

    private static async Task<IReadOnlyList<string>> RollbackAsync(List<IInstallStep> done, InstallContext ctx)
    {
        ctx.Log.Report("Desfazendo as alterações...");
        var rolledBack = new List<string>();
        for (var i = done.Count - 1; i >= 0; i--)
        {
            var step = done[i];
            try
            {
                // CancellationToken.None: um cancelamento não pode deixar o rollback pela metade.
                await step.RollbackAsync(ctx, CancellationToken.None);
                rolledBack.Add(step.Title);
            }
            catch (Exception ex)
            {
                ctx.Log.Report($"⚠ Não foi possível desfazer \"{step.Title}\": {ex.Message}");
            }
        }
        return rolledBack;
    }
}
