using Microsoft.Data.SqlClient;

namespace SiagroB1.Installer.Core.Migrations;

public sealed class SqlCompanyLister : ICompanyLister
{
    public async Task<IReadOnlyList<CompanyDatabase>> ListAsync(string commonConnectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(commonConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "IF OBJECT_ID('COMPANIES') IS NULL SELECT TOP 0 '' Code, '' CompanyName, '' ConnectionString " +
            "ELSE SELECT Code, CompanyName, ConnectionString FROM COMPANIES ORDER BY Code", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<CompanyDatabase>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CompanyDatabase(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return list;
    }
}

public sealed class SqlMigrationHistory : IMigrationHistory
{
    public async Task<IReadOnlySet<string>> AppliedAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "IF OBJECT_ID('__EFMigrationsHistory') IS NULL SELECT TOP 0 '' MigrationId " +
            "ELSE SELECT MigrationId FROM __EFMigrationsHistory", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            set.Add(reader.GetString(0));
        }
        return set;
    }
}

public static class SqlConnectionTester
{
    /// <summary>null = conectou; senão, a mensagem do SQL Server para mostrar ao usuário.</summary>
    public static async Task<string?> TestAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 10 };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);
            return null;
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
