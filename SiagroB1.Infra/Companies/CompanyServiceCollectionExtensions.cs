using Microsoft.Extensions.DependencyInjection;

namespace SiagroB1.Infra.Companies;

public static class CompanyServiceCollectionExtensions
{
    /// <summary>Catálogo (singleton) + contexto da execução (scoped). Registrar nos três hosts.</summary>
    public static IServiceCollection AddCompanyContext(this IServiceCollection services)
    {
        services.AddSingleton<ICompanyCatalog, CompanyCatalog>();
        services.AddScoped<ICompanyContext, CompanyContext>();
        return services;
    }
}
