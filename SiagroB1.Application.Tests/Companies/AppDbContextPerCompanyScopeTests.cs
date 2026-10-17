using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Prova o mecanismo que o Program.cs dos três hosts usa: AddDbContext com o overload (sp, options)
/// avalia as opções POR ESCOPO, então cada escopo abre o banco da sua empresa. Aqui o "banco" é um
/// InMemory nomeado pela connection string.
/// </summary>
public class AppDbContextPerCompanyScopeTests
{
    private static readonly CompanyInfo A = new("0001", "A", "db-a-" + Guid.NewGuid());
    private static readonly CompanyInfo B = new("0002", "B", "db-b-" + Guid.NewGuid());
    private static readonly string Fallback = "db-fallback-" + Guid.NewGuid();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", Fallback)));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseInMemoryDatabase(sp.GetRequiredService<ICompanyContext>().ConnectionString));
        return services.BuildServiceProvider();
    }

    private static int CountCountries(ServiceProvider provider, CompanyInfo? company, Action<AppDbContext>? act = null)
    {
        using var scope = provider.CreateScope();
        if (company is not null)
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        act?.Invoke(db);
        return db.Countries.Count();
    }

    [Fact]
    public void Each_scope_opens_the_database_of_its_company()
    {
        using var provider = Build();

        CountCountries(provider, A, db =>
        {
            db.Countries.Add(new Country { Code = "BR", Name = "Brasil", BacenCode = "1058" });
            db.SaveChanges();
        });

        Assert.Equal(1, CountCountries(provider, A));
        Assert.Equal(0, CountCountries(provider, B));
        Assert.Equal(0, CountCountries(provider, null));   // fallback da configuração
    }
}
