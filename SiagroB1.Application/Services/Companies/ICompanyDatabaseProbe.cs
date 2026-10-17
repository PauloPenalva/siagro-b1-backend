namespace SiagroB1.Application.Services.Companies;

public sealed record CompanyProbeResult(
    bool CanConnect, string? Error, IReadOnlyList<string> PendingMigrations, string Server, string Database);

/// <summary>Abre o banco de uma empresa candidata e diz se está com as migrations em dia. Interface para os testes não precisarem de SQL Server.</summary>
public interface ICompanyDatabaseProbe
{
    Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default);
}
