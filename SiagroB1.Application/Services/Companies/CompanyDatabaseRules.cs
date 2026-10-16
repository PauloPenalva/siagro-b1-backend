using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Companies;

public static class CompanyDatabaseRules
{
    public static async Task<CompanyProbeResult> ValidateConnectionStringAsync(
        string? connectionString, ICompanyDatabaseProbe probe, string? commonConnectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new DefaultException("Informe a string de conexão do banco da empresa.");

        if (CompanyConnectionStrings.IsMasked(connectionString))
            throw new DefaultException("Informe a senha real do banco: a string de conexão está com a senha mascarada.");

        if (CompanyConnectionStrings.SameDatabase(connectionString, commonConnectionString))
            throw new DefaultException("A string de conexão aponta para o banco COMMON. O banco de negócio da empresa tem de ser outro.");

        var result = await probe.ProbeAsync(connectionString, ct);

        if (!result.CanConnect)
            throw new DefaultException($"Não foi possível abrir o banco da empresa: {result.Error}");

        if (result.PendingMigrations.Count > 0)
            throw new DefaultException(
                $"O banco {result.Database} tem migrations pendentes ({string.Join(", ", result.PendingMigrations)}). " +
                "Aplique-as com `dotnet ef database update --context AppDbContext --connection \"<string de conexão>\"` " +
                "antes de cadastrar a empresa.");

        return result;
    }
}
