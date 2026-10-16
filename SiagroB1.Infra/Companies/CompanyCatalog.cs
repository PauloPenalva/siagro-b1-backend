using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Companies;

/// <summary>
/// Singleton com snapshot de COMPANIES e TTL curto. Lê a COMMON por um escopo próprio porque o
/// <see cref="CommonDbContext"/> é scoped. Edição de connection string existente demora até
/// <see cref="Ttl"/> para chegar aos outros processos (Gateway/Reports); empresa NOVA é imediata
/// porque o miss recarrega.
/// </summary>
public sealed class CompanyCatalog(IServiceScopeFactory scopeFactory, IConfiguration configuration) : ICompanyCatalog
{
    public const string DefaultCodeConfigurationKey = "Company:Code";
    public const string FallbackDefaultCode = "0001";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, CompanyInfo> _snapshot =
        new Dictionary<string, CompanyInfo>(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedAt = DateTime.MinValue;

    public string DefaultCompanyCode =>
        (configuration[DefaultCodeConfigurationKey] ?? FallbackDefaultCode).Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(force: false, ct);
        return _snapshot.Values.OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await EnsureFreshAsync(force: false, ct);
        if (_snapshot.TryGetValue(code, out var hit)) return hit;

        await EnsureFreshAsync(force: true, ct);
        return _snapshot.TryGetValue(code, out hit) ? hit : null;
    }

    public CompanyInfo? TryGetCached(string code) => _snapshot.TryGetValue(code, out var hit) ? hit : null;

    public void Invalidate() => _loadedAt = DateTime.MinValue;

    private async Task EnsureFreshAsync(bool force, CancellationToken ct)
    {
        if (!force && DateTime.UtcNow - _loadedAt < Ttl) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && DateTime.UtcNow - _loadedAt < Ttl) return;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();

            var rows = await db.Companies.AsNoTracking()
                .Select(c => new CompanyInfo(c.Code, c.CompanyName, c.ConnectionString))
                .ToListAsync(ct);

            _snapshot = rows.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
            _loadedAt = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
