using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Web.Market;

/// <summary>Escopo de DI por empresa para a sessão da CMA (mesmo padrão do CompanyScopedJobRunner).</summary>
public sealed class CmaCompanyScope(ICompanyCatalog catalog, IServiceScopeFactory scopeFactory) : ICmaCompanyScope
{
    public async Task<T> RunAsync<T>(string companyCode, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var company = await catalog.GetByCodeAsync(companyCode, cancellationToken)
            ?? throw new InvalidOperationException($"Empresa {companyCode} não encontrada no catálogo.");

        using var _ = CompanyAmbient.Use(company.Code);
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        return await work(scope.ServiceProvider, cancellationToken);
    }
}
