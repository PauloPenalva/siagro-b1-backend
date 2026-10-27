using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution.Steps;

public sealed class MigrateStep(MigrationRunner runner) : IInstallStep
{
    public string Title => "Atualizar os bancos de dados";

    public static MigrationRequest Request(InstallContext ctx)
    {
        var edits = ServiceSettingsMapper.ToEdits(ServiceKind.Web, ctx.Settings);
        return new MigrationRequest(
            ctx.MigrationsDir,
            ctx.Environment,
            edits["ConnectionStrings:SiagroCommon"]!.GetValue<string>(),
            edits["ConnectionStrings:SiagroDB"]!.GetValue<string>(),
            ctx.DdlUser,
            ctx.DdlPassword);
    }

    public async Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        if (ctx.MigrationReport?.HasPending == true && !ctx.DatabaseBackupConfirmed)
        {
            throw new InvalidOperationException("Há atualizações de banco pendentes e não foi confirmado o backup do banco de dados.");
        }
        await runner.ApplyAsync(Request(ctx), ctx.Log, ct);
    }

    public Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        ctx.Log.Report("⚠ Alterações já aplicadas no banco NÃO são desfeitas automaticamente. Use o backup do banco se precisar voltar.");
        return Task.CompletedTask;
    }
}
