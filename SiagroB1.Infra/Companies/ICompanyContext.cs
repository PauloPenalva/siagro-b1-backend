namespace SiagroB1.Infra.Companies;

/// <summary>
/// Empresa da execução corrente (scoped). Ordem de resolução: valor explícito do escopo (HTTP,
/// runner de jobs) → <see cref="CompanyAmbient"/> (filtro do Hangfire) → fallback da configuração
/// (<c>ConnectionStrings:SiagroDB</c>: design-time, guard de migrations, endpoints anônimos).
/// </summary>
public interface ICompanyContext
{
    /// <summary>Nulo quando nada foi resolvido (o fallback da configuração está em uso).</summary>
    CompanyInfo? Current { get; }

    bool IsResolved { get; }

    /// <summary>Código da empresa resolvida, ou o código padrão do catálogo.</summary>
    string CompanyCode { get; }

    string CompanyName { get; }

    /// <summary>Connection string do banco de negócio desta execução.</summary>
    string ConnectionString { get; }

    void Set(CompanyInfo company);
}
