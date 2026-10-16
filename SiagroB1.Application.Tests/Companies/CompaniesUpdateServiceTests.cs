using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompaniesUpdateServiceTests
{
    private const string Stored = "Server=db;Database=LM;User Id=app;Password=s3cr3t";

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCompanyDatabaseProbe _probe = new();
    private readonly FakeCompanyCatalog _catalog = new();

    public CompaniesUpdateServiceTests()
    {
        _db.Companies.Add(new Company { Code = "0002", CompanyName = "LM", ConnectionString = Stored });
        _db.SaveChanges();
    }

    private CompaniesUpdateService Create() => new(_db, _probe, _catalog, NullLogger<CompaniesUpdateService>.Instance);

    [Fact]
    public async Task Masked_password_keeps_the_stored_one_and_does_not_probe_when_nothing_else_changed()
    {
        var incoming = new Company { Code = "0002", CompanyName = "LM CEREAIS", ConnectionString = CompanyConnectionStrings.MaskPassword(Stored) };

        await Create().ExecuteAsync("0002", incoming);

        var row = _db.Companies.Single();
        Assert.Equal("LM CEREAIS", row.CompanyName);
        Assert.Contains("Password=s3cr3t", row.ConnectionString);
        Assert.Empty(_probe.Probed);
        Assert.Equal(1, _catalog.Invalidations);
    }

    [Fact]
    public async Task Changed_connection_string_is_probed_and_pending_migrations_are_refused()
    {
        _probe.Result = new(true, null, ["20261016_X"], "db", "LM2");
        var incoming = new Company { Code = "0002", CompanyName = "LM", ConnectionString = "Server=db;Database=LM2;User Id=app;Password=nova" };

        await Assert.ThrowsAsync<DefaultException>(() => Create().ExecuteAsync("0002", incoming));

        Assert.Single(_probe.Probed);
        Assert.Equal(Stored, _db.Companies.Single().ConnectionString);
    }

    [Fact]
    public async Task Code_is_immutable()
    {
        var incoming = new Company { Code = "9999", CompanyName = "LM", ConnectionString = Stored };

        await Create().ExecuteAsync("0002", incoming);

        Assert.Equal("0002", _db.Companies.Single().Code);
    }
}
