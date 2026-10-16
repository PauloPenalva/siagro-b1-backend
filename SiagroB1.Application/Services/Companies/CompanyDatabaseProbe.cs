using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public sealed class CompanyDatabaseProbe : ICompanyDatabaseProbe
{
    public async Task<CompanyProbeResult> ProbeAsync(string connectionString, CancellationToken ct = default)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return new CompanyProbeResult(false, $"String de conexão inválida: {e.Message}", [], string.Empty, string.Empty);
        }

        // Mesmo assembly de migrations do Web: é o que diz se o banco está no esquema que o código espera.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, b => b.MigrationsAssembly("SiagroB1.Migrations").CommandTimeout(15))
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
            return new CompanyProbeResult(true, null, pending, builder.DataSource, builder.InitialCatalog);
        }
        catch (Exception e)
        {
            return new CompanyProbeResult(false, e.Message, [], builder.DataSource, builder.InitialCatalog);
        }
    }
}
