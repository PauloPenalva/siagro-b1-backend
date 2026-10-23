using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Inclusão do MDF-e a partir dos CT-e autorizados: tudo o que dá para herdar vem preenchido.</summary>
public class TransportManifestsCreateFromCtesServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private TransportManifestsCreateFromCtesService Service(string erp = "STANDALONE") => new(_db, MdfeTestSupport.Config(erp));

    private Task<TransportManifest> LoadAsync(Guid key) => _db.Context.TransportManifests.AsNoTracking()
        .Include(m => m.Ctes).Include(m => m.LoadingCities).Include(m => m.Vehicles).Include(m => m.Contractors)
        .FirstAsync(m => m.Key == key);

    [Fact]
    public async Task Create_groups_cte_keys_by_unloading_city()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1, endCity: "3509502");
        var b = await MdfeTestSupport.AddCteAsync(_db, 2, endCity: "4106902");
        var c = await MdfeTestSupport.AddCteAsync(_db, 3, endCity: "3509502");

        var key = await Service().CreateFromCtesAsync("01", [a.Key, b.Key, c.Key], "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Draft, m.Status);
        Assert.Equal(3, m.CteCount);
        Assert.Equal("SP", m.UfStart);
        Assert.Equal("3550308", Assert.Single(m.LoadingCities).CityIbge);
        var byCity = m.Ctes.OrderBy(x => x.LineNum).GroupBy(x => x.UnloadingCityIbge).ToDictionary(g => g.Key, g => g.Select(x => x.CteAccessKey).ToList());
        Assert.Equal([a.AccessKey!, c.AccessKey!], byCity["3509502"]);
        Assert.Equal("CURITIBA", m.Ctes.First(x => x.UnloadingCityIbge == "4106902").UnloadingCityName);
        Assert.All(m.Ctes, x => Assert.True(x.IsActive));
        Assert.Equal("AV1", m.Ctes.First(x => x.TransportDocumentKey == a.Key).Endorsements);
        Assert.Equal(300m, m.CargoValue);
        Assert.Equal("05", m.CargoType);
        Assert.Equal("SOJA", m.ProductDescription);
        Assert.Equal("APOL1", m.PolicyNumber);
        Assert.Equal("52998224725", m.DriverCpf);
        var traction = Assert.Single(m.Vehicles, v => v.IsTraction);
        Assert.Equal("ABC1D23", traction.Plate);
        Assert.Equal("03", traction.WheelType);
        Assert.Equal(9000, traction.TareKg);
        Assert.Equal("TRL1A11", Assert.Single(m.Vehicles, v => !v.IsTraction).Plate);
    }

    [Fact]
    public async Task Create_refuses_cte_already_in_active_manifest()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 7);
        await Service().CreateFromCtesAsync("01", [a.Key], "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [a.Key], "tester"));

        Assert.StartsWith("O CT-e 7 já está no MDF-e", ex.Message);
    }

    [Fact]
    public async Task Create_refuses_different_traction_plates()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1, plate: "ABC1D23");
        var b = await MdfeTestSupport.AddCteAsync(_db, 2, plate: "XYZ9K88");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [a.Key, b.Key], "tester"));

        Assert.Equal("Os CT-e escolhidos são de veículos diferentes.", ex.Message);
    }

    [Fact]
    public async Task Create_refuses_different_start_ufs()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1);
        var b = await MdfeTestSupport.AddCteAsync(_db, 2, startCity: "4106902", startUf: "PR");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [a.Key, b.Key], "tester"));

        Assert.Equal("Os CT-e escolhidos começam em UFs diferentes.", ex.Message);
    }

    [Fact]
    public async Task Create_refuses_different_end_ufs()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1);
        var b = await MdfeTestSupport.AddCteAsync(_db, 2, endCity: "4106902", endUf: "PR");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [a.Key, b.Key], "tester"));

        Assert.Equal("Os CT-e escolhidos terminam em UFs diferentes.", ex.Message);
    }

    [Fact]
    public async Task Unique_violation_maps_to_cte_and_manifest_numbers()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 9);
        var rival = new TransportManifest { BranchCode = "01", Number = 55 };
        rival.Ctes.Add(new TransportManifestCte
        {
            TransportDocumentKey = cte.Key, CteAccessKey = cte.AccessKey!, UnloadingCityIbge = "3509502", UnloadingCityName = "CAMPINAS",
        });
        _db.Context.TransportManifests.Add(rival);
        await _db.SaveChangesAsync();

        var mapped = await MdfeEligibleCtesQuery.MapUniqueViolationAsync(_db, 2601, [cte.Key], Guid.NewGuid());
        var other = await MdfeEligibleCtesQuery.MapUniqueViolationAsync(_db, 547, [cte.Key], Guid.NewGuid());

        Assert.Equal("O CT-e 9 já está no MDF-e 55.", mapped?.Message);
        Assert.Null(other);
        Assert.Null(MdfeEligibleCtesQuery.SqlErrorNumber(new Microsoft.EntityFrameworkCore.DbUpdateException("x", new InvalidOperationException())));
    }

    [Fact]
    public async Task Create_prefills_weight_only_for_kg_or_ton_m3_leaves_zero()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var kg = await MdfeTestSupport.AddCteAsync(_db, 1, unit: "01", quantity: 1500m);
        var ton = await MdfeTestSupport.AddCteAsync(_db, 2, unit: "02", quantity: 2m);
        var m3 = await MdfeTestSupport.AddCteAsync(_db, 3, unit: "00", quantity: 40m);

        var mixed = await LoadAsync(await Service().CreateFromCtesAsync("01", [kg.Key, ton.Key], "tester"));
        var volume = await LoadAsync(await Service().CreateFromCtesAsync("01", [m3.Key], "tester"));

        Assert.Equal("01", mixed.WeightUnit);
        Assert.Equal(3500m, mixed.GrossWeight);
        Assert.Equal(0m, volume.GrossWeight); // 0 = vazio: o usuário digita em KG/TON
    }

    [Fact]
    public async Task Create_contractors_are_distinct_takers()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1, takerTaxId: "99888777000166", takerName: "A");
        var b = await MdfeTestSupport.AddCteAsync(_db, 2, takerTaxId: "99.888.777/0001-66", takerName: "A");
        var c = await MdfeTestSupport.AddCteAsync(_db, 3, takerTaxId: "52998224725", takerName: "B");

        var m = await LoadAsync(await Service().CreateFromCtesAsync("01", [a.Key, b.Key, c.Key], "tester"));

        Assert.Equal(["52998224725", "99888777000166"], m.Contractors.Select(x => x.TaxId).Order().ToList());
    }

    [Fact]
    public async Task Create_refuses_cte_not_authorized_or_other_branch()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var draft = await MdfeTestSupport.AddCteAsync(_db, 1, status: CteStatus.Draft);

        await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [draft.Key], "tester"));
    }

    [Fact]
    public async Task Create_refuses_non_endorsed_ctes_listing_them()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var ok = await MdfeTestSupport.AddCteAsync(_db, 1);
        var n2 = await MdfeTestSupport.AddCteAsync(_db, 2, endorsed: false);
        var n3 = await MdfeTestSupport.AddCteAsync(_db, 3, endorsed: false);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().CreateFromCtesAsync("01", [ok.Key, n2.Key, n3.Key], "tester"));

        Assert.Equal("Averbe os CT-e antes de incluir no MDF-e: 2, 3.", ex.Message);
        Assert.Empty(_db.Context.TransportManifests);
    }

    [Fact]
    public async Task Eligible_query_lists_non_endorsed_with_their_status()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        await MdfeTestSupport.AddCteAsync(_db, 1);
        await MdfeTestSupport.AddCteAsync(_db, 2, endorsed: false);

        var list = await new MdfeEligibleCtesQuery(_db, MdfeTestSupport.Config()).ListAsync("01");

        Assert.Equal([CteEndorsementStatus.Endorsed, CteEndorsementStatus.None], list.Select(x => x.EndorsementStatus).ToList());
    }

    [Fact]
    public async Task Eligible_query_skips_ctes_with_active_link()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var a = await MdfeTestSupport.AddCteAsync(_db, 1);
        var b = await MdfeTestSupport.AddCteAsync(_db, 2);
        await MdfeTestSupport.AddCteAsync(_db, 3, status: CteStatus.Cancelled);
        await Service().CreateFromCtesAsync("01", [a.Key], "tester");

        var list = await new MdfeEligibleCtesQuery(_db, MdfeTestSupport.Config()).ListAsync("01");

        Assert.Equal([b.Key], list.Select(x => x.Key).ToList());
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").CreateFromCtesAsync("01", [Guid.NewGuid()], "tester"));

        Assert.Equal(BranchMdfeSettingsService.StandaloneOnly, ex.Message);
    }
}
