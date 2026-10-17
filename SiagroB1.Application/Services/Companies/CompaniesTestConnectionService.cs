using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public class CompaniesTestConnectionService(CommonDbContext db, ICompanyDatabaseProbe probe)
{
    /// <param name="code">Empresa já cadastrada (para fundir a senha mascarada), ou nulo no cadastro novo.</param>
    public async Task<CompanyTestConnectionResult> ExecuteAsync(string? code, string connectionString)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var stored = await db.Companies.AsNoTracking().Where(c => c.Code == code).Select(c => c.ConnectionString).FirstOrDefaultAsync();
            if (stored is not null)
                connectionString = CompanyConnectionStrings.MergeMaskedPassword(connectionString, stored);
        }

        if (CompanyConnectionStrings.IsMasked(connectionString))
            return new CompanyTestConnectionResult { Success = false, Message = "Informe a senha real do banco para testar a conexão." };

        var result = await probe.ProbeAsync(connectionString);

        return new CompanyTestConnectionResult
        {
            Success = result.CanConnect && result.PendingMigrations.Count == 0,
            Server = result.Server,
            Database = result.Database,
            PendingMigrations = [.. result.PendingMigrations],
            Message = !result.CanConnect
                ? $"Falha ao conectar: {result.Error}"
                : result.PendingMigrations.Count > 0
                    ? $"Conectou em {result.Server}/{result.Database}, mas há {result.PendingMigrations.Count} migration(s) pendente(s)."
                    : $"Conexão OK em {result.Server}/{result.Database}. Banco com as migrations em dia.",
        };
    }
}
