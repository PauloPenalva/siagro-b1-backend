using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Qual MDF-e leva o CT-e: o ativo; sem ativo, o mais recente; sem nenhum, nulo.</summary>
public class CteManifestLinkQueryTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private CteManifestLinkQuery Query(string erp = "STANDALONE") => new(_db, MdfeTestSupport.Config(erp));

    private async Task AddManifestAsync(TransportDocument cte, int number, MdfeStatus status, DateTime createdAt)
    {
        var m = new TransportManifest { BranchCode = "01", Series = 1, Number = number, Status = status, CreatedAt = createdAt, CteCount = 1 };
        m.Ctes.Add(new TransportManifestCte
        {
            TransportDocumentKey = cte.Key, CteAccessKey = cte.AccessKey!, UnloadingCityIbge = "3509502",
            UnloadingCityName = "CAMPINAS", IsActive = MdfeStatuses.Active.Contains(status),
        });
        _db.Context.TransportManifests.Add(m);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_null_when_cte_has_no_manifest()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 1);
        Assert.Null(await Query().GetAsync(cte.Key));
    }

    [Fact]
    public async Task Prefers_active_manifest_over_newer_cancelled_one()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 1);
        await AddManifestAsync(cte, 10, MdfeStatus.Authorized, new DateTime(2026, 10, 1));
        await AddManifestAsync(cte, 11, MdfeStatus.Cancelled, new DateTime(2026, 10, 2));

        var link = await Query().GetAsync(cte.Key);

        Assert.Equal(10, link!.Number);
        Assert.Equal(MdfeStatus.Authorized, link.Status);
    }

    [Fact]
    public async Task Falls_back_to_latest_when_none_active()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 1);
        await AddManifestAsync(cte, 10, MdfeStatus.Cancelled, new DateTime(2026, 10, 1));
        await AddManifestAsync(cte, 11, MdfeStatus.Closed, new DateTime(2026, 10, 2));

        Assert.Equal(11, (await Query().GetAsync(cte.Key))!.Number);
    }

    [Fact]
    public async Task Rejects_non_standalone()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Query("SAPB1").GetAsync(Guid.NewGuid()));
    }
}
