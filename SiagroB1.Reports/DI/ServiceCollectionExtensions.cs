using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReportServices(this IServiceCollection services)
    {
        services.Scan(scan => scan
            // Busca em todos os assemblies do projeto
            .FromAssembliesOf(
                typeof(IFastReportService),
                typeof(FastReportService)) 
            // Registra Services
            .AddClasses(classes => classes
                .Where(t => t.Name.EndsWith("Service") &&
                            !t.IsInterface && !t.IsAbstract))
            .AsImplementedInterfaces()
            .WithScopedLifetime());
        
        services.Scan(scan => scan
            .FromAssemblyOf<FastReportService>()
            .AddClasses(classes => classes.Where(t => t.Name.EndsWith("Service")))
            .AsSelf()
            .WithScopedLifetime());
            
        return services;
    }
}