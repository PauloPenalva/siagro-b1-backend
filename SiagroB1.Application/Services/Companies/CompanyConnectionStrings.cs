using Microsoft.Data.SqlClient;

namespace SiagroB1.Application.Services.Companies;

/// <summary>A senha do banco nunca sai do servidor: GET devolve mascarada, e a edição que volta mascarada mantém a armazenada.</summary>
public static class CompanyConnectionStrings
{
    public const string Mask = "*****";

    public static string MaskPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString ?? string.Empty;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(builder.Password)) return connectionString;
            builder.Password = Mask;
            return builder.ConnectionString;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return connectionString;
        }
    }

    public static bool IsMasked(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            return new SqlConnectionStringBuilder(connectionString).Password == Mask;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }

    public static string MergeMaskedPassword(string incoming, string stored)
    {
        if (!IsMasked(incoming)) return incoming;

        var builder = new SqlConnectionStringBuilder(incoming)
        {
            Password = new SqlConnectionStringBuilder(stored).Password,
        };
        return builder.ConnectionString;
    }

    /// <summary>Mesmo servidor e catálogo — para recusar a COMMON como banco de negócio.</summary>
    public static bool SameDatabase(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            var x = new SqlConnectionStringBuilder(a);
            var y = new SqlConnectionStringBuilder(b);
            return string.Equals(x.DataSource, y.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.InitialCatalog, y.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Mesma string de conexão, ignorando ordem/sinônimos de chaves (o builder normaliza). Decide se a
    /// edição mexeu no banco — <see cref="MergeMaskedPassword"/> devolve a forma normalizada, então a
    /// comparação textual acusaria mudança sempre. Se algum lado não parseia, compara o texto.
    /// </summary>
    public static bool AreEquivalent(string? a, string? b)
    {
        if (a is null || b is null) return a == b;

        try
        {
            return new SqlConnectionStringBuilder(a).EquivalentTo(new SqlConnectionStringBuilder(b));
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }
    }
}
