using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>
/// Ordem de resolução da empresa: explícita (HTTP) → ambiente (jobs) → fallback da configuração.
/// Empresa ambiente desconhecida LANÇA: processar um job no banco errado é pior que falhar.
/// </summary>
public class CompanyContextTests
{
    private static readonly CompanyInfo A = new("0001", "CEAGUI", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "LM CEREAIS", "Server=.;Database=B");

    private static CompanyContext Create(FakeCompanyCatalog? catalog = null) =>
        new(catalog ?? new FakeCompanyCatalog(A, B),
            TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK"), ("CompanyName", "DA CONFIG")),
            NullLogger<CompanyContext>.Instance);

    [Fact]
    public void Without_anything_set_uses_configuration_fallback()
    {
        var context = Create();

        Assert.False(context.IsResolved);
        Assert.Null(context.Current);
        Assert.Equal("Server=.;Database=FALLBACK", context.ConnectionString);
        Assert.Equal("0001", context.CompanyCode);
        Assert.Equal("DA CONFIG", context.CompanyName);
    }

    [Fact]
    public void Explicit_company_wins()
    {
        var context = Create();
        context.Set(B);

        Assert.True(context.IsResolved);
        Assert.Equal("Server=.;Database=B", context.ConnectionString);
        Assert.Equal("0002", context.CompanyCode);
        Assert.Equal("LM CEREAIS", context.CompanyName);
    }

    [Fact]
    public void Ambient_company_is_used_when_nothing_explicit()
    {
        using var _ = CompanyAmbient.Use("0002");
        var context = Create();

        Assert.Equal("Server=.;Database=B", context.ConnectionString);
    }

    [Fact]
    public void Explicit_beats_ambient()
    {
        using var _ = CompanyAmbient.Use("0002");
        var context = Create();
        context.Set(A);

        Assert.Equal("Server=.;Database=A", context.ConnectionString);
    }

    [Fact]
    public void Ambient_company_unknown_to_catalog_throws()
    {
        using var _ = CompanyAmbient.Use("9999");
        var context = Create();

        var ex = Assert.Throws<InvalidOperationException>(() => context.ConnectionString);
        Assert.Contains("9999", ex.Message);
    }

    [Fact]
    public void Ambient_Use_restores_previous_value()
    {
        Assert.Null(CompanyAmbient.CompanyCode);
        using (CompanyAmbient.Use("0001"))
        {
            Assert.Equal("0001", CompanyAmbient.CompanyCode);
            using (CompanyAmbient.Use("0002"))
                Assert.Equal("0002", CompanyAmbient.CompanyCode);
            Assert.Equal("0001", CompanyAmbient.CompanyCode);
        }
        Assert.Null(CompanyAmbient.CompanyCode);
    }

    [Fact]
    public void Set_after_connection_consumed_logs_warning_and_keeps_new_company()
    {
        var logger = new TestLogger<CompanyContext>();
        var context = new CompanyContext(new FakeCompanyCatalog(A, B),
            TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")), logger);

        _ = context.ConnectionString;   // AppDbContext do escopo já nasceu no fallback
        context.Set(B);

        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("0002"));
        Assert.Equal("0002", context.CompanyCode);
    }
}
