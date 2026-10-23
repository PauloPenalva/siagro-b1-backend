using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Gravação do rascunho do MDF-e.</summary>
public class TransportManifestsSaveServiceTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly UnitOfWork _db;

    public TransportManifestsSaveServiceTests() => _db = TestDb.CreateUnitOfWork(_name);

    private readonly FakeMdfeNumberReservationService _numbers = new(50);

    private TransportManifestsSaveService Save(UnitOfWork? db = null) =>
        new(db ?? _db, MdfeTestSupport.Config(), new MdfeReadinessValidator(db ?? _db), _numbers);

    private async Task<(Guid Manifest, TransportDocument Cte)> DraftAsync(bool requiresCiot = false)
    {
        await MdfeTestSupport.SeedBranchAsync(_db, requiresCiot);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 7);
        var key = await new TransportManifestsCreateFromCtesService(_db, MdfeTestSupport.Config()).CreateFromCtesAsync("01", [cte.Key], "tester");
        return (key, cte);
    }

    [Fact]
    public async Task Create_rechecks_cte_eligibility_inside_transaction()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 7);
        // Outra aba gravou um MDF-e com o mesmo CT-e (outro contexto, mesmo banco).
        var other = TestDb.CreateUnitOfWork(_name);
        var rival = new TransportManifest { BranchCode = "01", Number = 42 };
        rival.Ctes.Add(new TransportManifestCte
        {
            TransportDocumentKey = cte.Key, CteAccessKey = cte.AccessKey!, UnloadingCityIbge = "3509502", UnloadingCityName = "CAMPINAS",
        });
        other.Context.TransportManifests.Add(rival);
        await other.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new TransportManifestsCreateFromCtesService(TestDb.CreateUnitOfWork(_name), MdfeTestSupport.Config())
                .CreateFromCtesAsync("01", [cte.Key], "tester"));

        Assert.Equal("O CT-e 7 já está no MDF-e 42.", ex.Message);
    }

    [Fact]
    public async Task Save_ignores_null_driver_cpf()
    {
        var (key, _) = await DraftAsync();

        await Save().SaveAsync(key, new TransportManifestSaveModel { AdditionalDrivers = [new() { Cpf = null!, Name = "X" }] }, "tester");

        Assert.False(await _db.Context.TransportManifestDrivers.AnyAsync());
    }

    [Fact]
    public async Task Save_updates_editable_fields_and_normalizes_digits()
    {
        var (key, _) = await DraftAsync();

        await Save().SaveAsync(key, new TransportManifestSaveModel
        {
            RouteUfs = ["mg", "GO"], Seals = ["L1"], LoadingZip = "01001-000", UnloadingZip = "13010-000", GrossWeight = 2, WeightUnit = "02",
            CiotNumber = "1234.5678.9012", CiotResponsibleTaxId = "12.345.678/0001-95",
            Contractors = [new TransportManifestContractorModel { TaxId = "529.982.247-25", Name = "B" }],
            Payments = [new TransportManifestPaymentModel
            {
                ContractorTaxId = "529.982.247-25", ContractorName = "B", Total = 10, Components = [new() { Type = "99", Value = 10, Description = "FRETE" }],
            }],
            AdditionalInfo = "  OBS  ",
        }, "tester");

        var m = await _db.Context.TransportManifests.AsNoTracking().Include(x => x.RouteUfs).Include(x => x.Contractors)
            .Include(x => x.Payments).ThenInclude(p => p.Components).FirstAsync(x => x.Key == key);
        Assert.Equal(["MG", "GO"], m.RouteUfs.OrderBy(r => r.LineNum).Select(r => r.Uf).ToList());
        Assert.Equal("01001000", m.LoadingZip);
        Assert.Equal("123456789012", m.CiotNumber);
        Assert.Equal("12345678000195", m.CiotResponsibleTaxId);
        Assert.Equal("52998224725", Assert.Single(m.Contractors).TaxId);
        Assert.Equal(10m, Assert.Single(Assert.Single(m.Payments).Components).Value);
        Assert.Equal("OBS", m.AdditionalInfo);
        Assert.Equal(1, m.CteCount);
    }

    [Fact]
    public async Task Save_refreshes_empty_insurance_from_branch_settings()
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        var settings = await _db.Context.BranchTransportSettings.SingleAsync();
        settings.InsurerTaxId = null; settings.InsurerName = null; settings.PolicyNumber = null; settings.InsuranceResponsible = null;
        await _db.SaveChangesAsync();
        var cte = await MdfeTestSupport.AddCteAsync(_db, 7);
        var key = await new TransportManifestsCreateFromCtesService(_db, MdfeTestSupport.Config()).CreateFromCtesAsync("01", [cte.Key], "tester");
        Assert.Null((await _db.Context.TransportManifests.AsNoTracking().SingleAsync()).InsurerTaxId);

        settings.InsuranceResponsible = InsuranceResponsible.Issuer; settings.InsurerTaxId = "11222333000181";
        settings.InsurerName = "SEGURADORA"; settings.PolicyNumber = "APOL1";
        await _db.SaveChangesAsync();
        await Save().SaveAsync(key, new TransportManifestSaveModel { GrossWeight = 1 }, "tester");

        var m = await _db.Context.TransportManifests.AsNoTracking().SingleAsync();
        Assert.Equal("1", m.InsuranceResponsible);
        Assert.Equal("11222333000181", m.InsurerTaxId);
        Assert.Equal("SEGURADORA", m.InsurerName);
        Assert.Equal("APOL1", m.PolicyNumber);
    }

    [Fact]
    public async Task Save_warns_when_branch_requires_ciot()
    {
        var (key, _) = await DraftAsync(requiresCiot: true);

        var warnings = await Save().SaveAsync(key, new TransportManifestSaveModel { GrossWeight = 1 }, "tester");

        Assert.Contains(MdfeReadinessValidator.CiotRequiredWarning, warnings);
    }

    [Fact]
    public async Task Delete_draft_frees_the_cte()
    {
        var (key, cte) = await DraftAsync();

        await Save().DeleteAsync(key);

        Assert.False(await _db.Context.TransportManifestCtes.AnyAsync(x => x.TransportDocumentKey == cte.Key));
    }

    [Fact]
    public async Task Save_refuses_while_emission_lock_is_held()
    {
        var (key, _) = await DraftAsync();
        _numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() =>
            Save().SaveAsync(key, new TransportManifestSaveModel { GrossWeight = 99 }, "tester"));
        _numbers.EmissionBusy = false;
        Assert.NotEqual(99, (await _db.Context.TransportManifests.AsNoTracking().SingleAsync(x => x.Key == key)).GrossWeight);
    }

    [Fact]
    public async Task Save_takes_emission_lock_and_releases_it()
    {
        var (key, _) = await DraftAsync();
        var locksDuring = -1;
        _db.Context.SavingChanges += (_, _) => locksDuring = _numbers.LocksHeld;

        await Save().SaveAsync(key, new TransportManifestSaveModel { GrossWeight = 99 }, "tester");

        Assert.Equal(1, locksDuring);
        Assert.Equal(0, _numbers.LocksHeld);
    }
}
