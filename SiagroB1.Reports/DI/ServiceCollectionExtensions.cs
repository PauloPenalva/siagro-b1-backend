using SiagroB1.Reports.ReportGenerators;
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

        services.AddScoped<StorageStatementReportGenerator>();

        // Não termina em "Service": o scan acima não o registra.
        services.AddScoped<SiagroB1.Reports.Services.SpedFiscal.SpedFiscalDataLoader>();
        services.AddScoped<SiagroB1.Reports.Services.Sped.SpedMasterDataLoader>();
        services.AddScoped<SiagroB1.Reports.Services.EfdContributions.EfdContributionsDataLoader>();
        services.AddScoped<SiagroB1.Reports.Services.Ecd.EcdDataLoader>();
        // EcdFileService entra pelo scan; EcdLedger e EcdValidator são estáticos.
        services.AddScoped<SiagroB1.Reports.Services.Ecf.EcfDataLoader>();
        // EcfFileService entra pelo scan; EcfAssessment, EcfValidator e IncomeTaxCalculator são estáticos.
            
        return services;
    }
}