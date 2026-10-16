namespace SiagroB1.Infra.Companies;

/// <summary>Catálogo de empresas (COMPANIES na COMMON), cacheado por processo.</summary>
public interface ICompanyCatalog
{
    Task<IReadOnlyList<CompanyInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Miss no cache recarrega UMA vez antes de devolver nulo: empresa cadastrada no Web aparece no Gateway/Reports sem esperar o TTL.</summary>
    Task<CompanyInfo?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Leitura síncrona do snapshot — para o caminho dos jobs, que resolve a empresa dentro de um getter.</summary>
    CompanyInfo? TryGetCached(string code);

    /// <summary><c>Company:Code</c> da configuração; padrão "0001".</summary>
    string DefaultCompanyCode { get; }

    void Invalidate();
}
