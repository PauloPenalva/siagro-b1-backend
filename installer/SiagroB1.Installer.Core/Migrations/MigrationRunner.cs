using Microsoft.Data.SqlClient;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Migrations;

public sealed record CompanyDatabase(string Code, string Name, string ConnectionString);

public interface ICompanyLister
{
    Task<IReadOnlyList<CompanyDatabase>> ListAsync(string commonConnectionString, CancellationToken ct);
}

public interface IMigrationHistory
{
    /// <summary>MigrationIds já aplicados; conjunto vazio se o banco ainda não tem __EFMigrationsHistory.</summary>
    Task<IReadOnlySet<string>> AppliedAsync(string connectionString, CancellationToken ct);

    /// <summary>O banco (Initial Catalog) existe no servidor? Consulta pelo master.</summary>
    Task<bool> DatabaseExistsAsync(string connectionString, CancellationToken ct);
}

/// <param name="BundleDir">Pasta com efbundle-*.exe e as listas migrations-*.txt.</param>
/// <param name="DdlUser">Usuário só para a migração (permissão de DDL); não é gravado em appsettings.</param>
public sealed record MigrationRequest(
    string BundleDir,
    string Environment,
    string CommonConnectionString,
    string SiagroDbConnectionString,
    string? DdlUser,
    string? DdlPassword);

public sealed record PendingMigrations(string Label, IReadOnlyList<string> Migrations);

public sealed record MigrationReport(IReadOnlyList<PendingMigrations> Pending)
{
    public bool HasPending => Pending.Any(p => p.Migrations.Count > 0);
}

public sealed class MigrationFailedException(string label, int exitCode, string maskedOutput)
    : Exception($"A atualização do banco \"{label}\" falhou (código {exitCode}). {maskedOutput}".Trim())
{
    public string Label { get; } = label;
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Aplica as migrations pelos bundles gerados no build: primeiro o Common, depois o banco de cada
/// empresa cadastrada em COMPANIES (o PendingMigrationsGuard do Web checa todas).
/// </summary>
public sealed class MigrationRunner(IProcessRunner runner, ICompanyLister companies, IMigrationHistory history)
{
    public const string CommonBundle = "efbundle-common.exe";
    public const string AppBundle = "efbundle-app.exe";
    public const string CommonListFile = "migrations-common.txt";
    public const string AppListFile = "migrations-app.txt";
    private const string CommonLabel = "Common";

    public async Task<MigrationReport> ListPendingAsync(MigrationRequest request, CancellationToken ct)
    {
        var commonAll = ReadList(request.BundleDir, CommonListFile);
        var appAll = ReadList(request.BundleDir, AppListFile);
        var pending = new List<PendingMigrations>();

        var commonCs = WithDdlUser(request.CommonConnectionString, request);
        var commonApplied = await history.AppliedAsync(commonCs, ct);
        pending.Add(new PendingMigrations(CommonLabel, commonAll.Where(m => !commonApplied.Contains(m)).ToList()));

        foreach (var company in await Targets(request, ct))
        {
            var applied = await history.AppliedAsync(company.ConnectionString, ct);
            pending.Add(new PendingMigrations(company.Name, appAll.Where(m => !applied.Contains(m)).ToList()));
        }

        return new MigrationReport(pending);
    }

    public async Task ApplyAsync(MigrationRequest request, IProgress<string> log, CancellationToken ct)
    {
        await RunBundle(request, CommonBundle, CommonLabel, WithDdlUser(request.CommonConnectionString, request), log, ct);

        // Lista as empresas só depois do Common: numa instalação nova COMPANIES nem existe antes.
        foreach (var company in await Targets(request, ct))
        {
            await RunBundle(request, AppBundle, company.Name, company.ConnectionString, log, ct);
        }
    }

    private async Task<IReadOnlyList<CompanyDatabase>> Targets(MigrationRequest request, CancellationToken ct)
    {
        // Erro aqui (login, permissão, timeout) sobe: cair na empresa padrão deixaria as outras
        // empresas sem migration e o Web recusaria subir. COMPANIES inexistente já volta vazio.
        var list = await companies.ListAsync(WithDdlUser(request.CommonConnectionString, request), ct);

        // Banco sem empresa cadastrada: o Gateway cria a empresa padrão com ConnectionStrings:SiagroDB.
        if (list.Count == 0)
        {
            list = [new CompanyDatabase("", "Empresa padrão", request.SiagroDbConnectionString)];
        }

        return list
            .Select(c => c with { ConnectionString = WithDdlUser(c.ConnectionString, request) })
            .ToList();
    }

    private async Task RunBundle(MigrationRequest request, string bundle, string label, string connectionString, IProgress<string> log, CancellationToken ct)
    {
        // O bundle do EF cria o banco se ele não existir: um nome errado em COMPANIES viraria um banco
        // novo com todas as migrations aplicadas do zero. Banco inexistente é erro, nunca criação.
        if (!await history.DatabaseExistsAsync(connectionString, ct))
        {
            var target = new SqlConnectionStringBuilder(connectionString);
            throw new MigrationFailedException(label, -1,
                $"O banco \"{target.InitialCatalog}\" não existe no servidor \"{target.DataSource}\". Confira a configuração da empresa; o instalador não cria bancos.");
        }

        log.Report($"Atualizando banco \"{label}\"...");
        var result = await runner.RunAsync(
            Path.Combine(request.BundleDir, bundle),
            ["--connection", connectionString],
            request.BundleDir,
            // O bundle sobe o host do Web para achar os DbContexts, e o host exige SiagroDB/SiagroCommon
            // na configuração. Rodar na pasta dos bundles (sem appsettings) com as connection strings
            // por variável de ambiente não depende dos arquivos do cliente nem do modo ERP deles.
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = request.Environment,
                ["ConnectionStrings__SiagroDB"] = connectionString,
                ["ConnectionStrings__SiagroCommon"] = connectionString
            },
            ct);

        var output = SecretMasker.MaskText(result.Output).Trim();
        if (output.Length > 0)
        {
            log.Report(output);
        }
        if (result.ExitCode != 0)
        {
            throw new MigrationFailedException(label, result.ExitCode, output);
        }
        log.Report($"Banco \"{label}\" aplicado.");
    }

    private static string WithDdlUser(string connectionString, MigrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DdlUser))
        {
            return connectionString;
        }
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            IntegratedSecurity = false,
            UserID = request.DdlUser,
            Password = request.DdlPassword ?? ""
        };
        return builder.ConnectionString;
    }

    private static IReadOnlyList<string> ReadList(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : [];
    }
}
