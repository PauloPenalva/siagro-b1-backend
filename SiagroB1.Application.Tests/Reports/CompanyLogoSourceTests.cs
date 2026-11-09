using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Logo do cadastro de Empresas para os relatórios. A mesma instância de bytes precisa voltar
/// enquanto o logo não muda: o DANFE guarda o encaixe por instância, e uma cópia nova a cada
/// nota vazaria memória no libgdiplus (Linux).
/// </summary>
public class CompanyLogoSourceTests
{
    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private void SetLogo(string code, byte[] content)
    {
        var row = _db.CompanyLogos.FirstOrDefault(l => l.CompanyCode == code);
        if (row is null) _db.CompanyLogos.Add(new CompanyLogo { CompanyCode = code, Content = content, ContentType = "image/png" });
        else row.Content = content;
        _db.SaveChanges();
    }

    private DbCompanyLogoSource Create(CompanyLogoCache cache) => new(_db, cache, new TestLogger<DbCompanyLogoSource>());

    [Fact]
    public void Returns_the_registered_logo_and_null_without_one()
    {
        SetLogo("0001", [1, 2, 3]);
        var source = Create(new CompanyLogoCache(TimeSpan.FromMinutes(1)));

        Assert.Equal([1, 2, 3], source.Get("0001"));
        Assert.Null(source.Get("0002"));
    }

    [Fact]
    public void Same_instance_is_returned_while_cached()
    {
        SetLogo("0001", [1, 2, 3]);
        var cache = new CompanyLogoCache(TimeSpan.FromMinutes(1));

        Assert.Same(Create(cache).Get("0001"), Create(cache).Get("0001"));
    }

    [Fact]
    public void Unchanged_logo_keeps_the_instance_after_expiry()
    {
        SetLogo("0001", [1, 2, 3]);
        var cache = new CompanyLogoCache(TimeSpan.Zero);

        var first = Create(cache).Get("0001");

        Assert.Same(first, Create(cache).Get("0001"));
    }

    [Fact]
    public void Changed_logo_is_picked_up_after_expiry()
    {
        SetLogo("0001", [1, 2, 3]);
        var cache = new CompanyLogoCache(TimeSpan.Zero);
        Create(cache).Get("0001");

        SetLogo("0001", [9, 9]);

        Assert.Equal([9, 9], Create(cache).Get("0001"));
    }

    [Fact]
    public void Database_failure_returns_null_instead_of_breaking_the_report()
    {
        var cache = new CompanyLogoCache(TimeSpan.FromMinutes(1));
        var source = Create(cache);
        _db.Dispose();

        Assert.Null(source.Get("0001"));
    }
}
