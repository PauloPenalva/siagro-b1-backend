using SiagroB1.Application.Services;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.Scan(scan => scan
            // Busca em todos os assemblies do projeto
            .FromAssembliesOf(
                typeof(IBranchService), // Domain
                typeof(BranchService)) // Application
            // Registra Services
            .AddClasses(classes => classes
                .Where(t => t.Name.EndsWith("Service") &&
                            !t.IsInterface && !t.IsAbstract))
            .AsImplementedInterfaces()
            .WithScopedLifetime());
        
        services.Scan(scan => scan
            .FromAssemblyOf<PurchaseContractService>()
            .AddClasses(classes => classes.Where(t => t.Name.EndsWith("Service")))
            .AsSelf()
            .WithScopedLifetime());
            
        return services;
    }
}