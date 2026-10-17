using Microsoft.Data.SqlClient;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>
/// A trava de emissão (sp_getapplock, dono "Session") só é solta quando a SESSÃO termina: a conexão
/// dela não pode voltar para o pool, e a string tem de trazer a senha — e tem de ser a da EMPRESA
/// da execução, não a da configuração.
/// </summary>
public class NfeNumberReservationServiceTests
{
    [Fact]
    public void Emission_lock_connection_comes_from_the_company_context_and_never_from_the_pool()
    {
        const string company = "Server=db;Database=LMCEREAIS;User Id=app;Password=secret;TrustServerCertificate=True";
        // A conexão da requisição, depois de aberta, devolve a string SEM a senha (Persist Security Info=False).
        using var requestConnection = new SqlConnection("Server=db;Database=LMCEREAIS;User Id=app;TrustServerCertificate=True");

        var lockConnectionString = new NfeNumberReservationService(requestConnection, new FakeCompanyContext("0002", company))
            .EmissionLockConnectionString();

        var builder = new SqlConnectionStringBuilder(lockConnectionString);
        Assert.False(builder.Pooling);
        Assert.Equal("secret", builder.Password);
        Assert.Equal("LMCEREAIS", builder.InitialCatalog);
    }
}
