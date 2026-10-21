using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Application.Tests.Startup;

/// <summary>
/// Os serviços de amarração contábil têm que resolver nos dois modos (SAPB1 e STANDALONE), pois os
/// conjuntos OData e os validadores comuns dependem deles.
/// </summary>
public class DimensionServicesRegistrationTests
{
    [Fact]
    public void Dimension_services_resolve_without_standalone_registrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUnitOfWork>(_ => TestDb.CreateUnitOfWork());
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DistributionRuleService>(scope.ServiceProvider.GetRequiredService<IDistributionRuleService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DistributionRuleExpander>());
    }
}
