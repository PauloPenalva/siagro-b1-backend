namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Executa um trecho dentro da empresa: escopo de DI novo com CompanyAmbient + ICompanyContext definidos
/// (padrão do CompanyScopedJobRunner). Abstraído para a sessão da CMA ser testável sem host.
/// </summary>
public interface ICmaCompanyScope
{
    Task<T> RunAsync<T>(string companyCode, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
