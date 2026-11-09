using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Reports.Services;

/// <summary>Logo cadastrado na tela de Empresas (COMPANY_LOGOS), ou nulo.</summary>
public interface ICompanyLogoSource
{
    byte[]? Get(string companyCode);
}

/// <summary>
/// Cache por empresa (singleton). Ao expirar relê o banco, mas devolve a MESMA instância quando o
/// conteúdo não mudou: o DANFE guarda o encaixe do logo por instância de bytes, e uma cópia nova
/// a cada nota vazaria ~3 MB por DANFE no libgdiplus (Linux).
/// </summary>
public sealed class CompanyLogoCache(TimeSpan ttl)
{
    private readonly ConcurrentDictionary<string, (byte[]? Content, DateTime LoadedAt)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? GetOrLoad(string code, Func<byte[]?> load)
    {
        if (_entries.TryGetValue(code, out var entry) && DateTime.UtcNow - entry.LoadedAt < ttl)
            return entry.Content;

        var loaded = load();
        var content = entry.Content is not null && loaded is not null && entry.Content.AsSpan().SequenceEqual(loaded)
            ? entry.Content
            : loaded;

        _entries[code] = (content, DateTime.UtcNow);
        return content;
    }
}

public sealed class DbCompanyLogoSource(CommonDbContext db, CompanyLogoCache cache, ILogger<DbCompanyLogoSource> logger)
    : ICompanyLogoSource
{
    public byte[]? Get(string companyCode)
    {
        try
        {
            return cache.GetOrLoad(companyCode, () => db.CompanyLogos.AsNoTracking()
                .Where(l => l.CompanyCode == companyCode)
                .Select(l => l.Content)
                .FirstOrDefault());
        }
        catch (Exception ex)
        {
            // Um logo é enfeite: falha no banco cai para o arquivo configurado, nunca derruba o relatório.
            logger.LogWarning(ex, "Could not read the company logo from the database; falling back to the configured file.");
            return null;
        }
    }
}
