using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>
/// A trava de emissão (sp_getapplock, dono "Session") só é solta quando a SESSÃO termina: a conexão
/// dela não pode voltar para o pool, e a string tem de trazer a senha.
/// </summary>
public class NfeNumberReservationServiceTests
{
    [Fact]
    public void Emission_lock_connection_comes_from_configuration_and_never_from_the_pool()
    {
        const string configured = "Server=db;Database=SIAGRO;User Id=app;Password=secret;TrustServerCertificate=True";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SiagroDB"] = configured })
            .Build();
        // A conexão da requisição, depois de aberta, devolve a string SEM a senha (Persist Security Info=False).
        using var requestConnection = new SqlConnection("Server=db;Database=SIAGRO;User Id=app;TrustServerCertificate=True");

        var lockConnectionString = new NfeNumberReservationService(requestConnection, configuration)
            .EmissionLockConnectionString();

        var builder = new SqlConnectionStringBuilder(lockConnectionString);
        Assert.False(builder.Pooling);
        Assert.Equal("secret", builder.Password);
        Assert.Equal("SIAGRO", builder.InitialCatalog);
    }
}
