using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contexto de empresa fixo. <see cref="Unresolved"/> simula o fallback da configuração.</summary>
public sealed class FakeCompanyContext(string code = "0001", string? connectionString = null) : ICompanyContext
{
    public CompanyInfo? Current { get; private set; } =
        new(code, $"Empresa {code}", connectionString ?? $"Server=.;Database=DB_{code};Integrated Security=True");

    public static FakeCompanyContext Unresolved() => new() { Current = null };

    public bool IsResolved => Current is not null;
    public string CompanyCode => Current?.Code ?? "0001";
    public string CompanyName => Current?.CompanyName ?? string.Empty;
    public string ConnectionString => Current?.ConnectionString ?? "Server=.;Database=FALLBACK;Integrated Security=True";

    public void Set(CompanyInfo company) => Current = company;
}
