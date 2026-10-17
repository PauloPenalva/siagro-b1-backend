using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Support;

public sealed class FakeCompanyCatalog(params CompanyInfo[] companies) : ICompanyCatalog
{
    public List<CompanyInfo> Companies { get; } = [.. companies];
    public int Invalidations { get; private set; }
    public string DefaultCompanyCode { get; set; } = "0001";

    public Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CompanyInfo>>(Companies.OrderBy(c => c.Code).ToList());

    public Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(TryGetCached(code));

    public CompanyInfo? TryGetCached(string code) =>
        Companies.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public void Invalidate() => Invalidations++;
}
