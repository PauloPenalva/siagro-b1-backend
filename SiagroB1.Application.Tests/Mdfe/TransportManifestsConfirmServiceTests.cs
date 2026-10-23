using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Confirmar / Reabrir / Descartar número do MDF-e: número e chave nascem uma vez e não mudam.</summary>
public class TransportManifestsConfirmServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(50);

    private TransportManifestsConfirmService Confirm(string erp = "STANDALONE", Func<DateTimeOffset>? clock = null) =>
        new(_db, MdfeTestSupport.Config(erp), _numbers, new MdfeReadinessValidator(_db), clock);

    private TransportManifestsReopenService Reopen() => new(_db, MdfeTestSupport.Config(), _numbers);

    private TransportManifestsDiscardNumberService Discard() => new(_db, MdfeTestSupport.Config(), _numbers);

    private async Task<Guid> SeedReadyAsync(decimal weight = 10, string unit = "01", bool enabled = true)
    {
        await MdfeTestSupport.SeedBranchAsync(_db);
        if (!enabled) (await _db.Context.BranchMdfeSettings.SingleAsync()).Enabled = false;
        await _db.SaveChangesAsync();
        var cte = await MdfeTestSupport.AddCteAsync(_db, 7);
        var key = await new TransportManifestsCreateFromCtesService(_db, MdfeTestSupport.Config()).CreateFromCtesAsync("01", [cte.Key], "tester");
        await new TransportManifestsSaveService(_db, MdfeTestSupport.Config(), new MdfeReadinessValidator(_db), _numbers).SaveAsync(key,
            new TransportManifestSaveModel { GrossWeight = weight, WeightUnit = unit, LoadingZip = "01001000", UnloadingZip = "13010000",
                Ncm = "12019000", CiotNumber = "123456789012", CiotResponsibleTaxId = "11222333000181",
                Payments = [new TransportManifestPaymentModel { ContractorTaxId = "52998224725", ContractorName = "B", Total = 1, Components = [new() { Type = "99", Value = 1, Description = "FRETE" }] }],
            }, "tester");
        return key;
    }

    private Task<TransportManifest> LoadAsync(Guid key) =>
        _db.Context.TransportManifests.AsNoTracking().Include(x => x.Ctes).SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Confirm_reserves_number_and_builds_model_58_key()
    {
        var key = await SeedReadyAsync();

        await Confirm().ConfirmAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Confirmed, m.Status);
        Assert.Equal(50, m.Number);
        Assert.Equal(8, m.RandomCode!.Length);
        Assert.NotEqual("00000050", m.RandomCode);
        Assert.Equal(44, m.AccessKey!.Length);
        Assert.StartsWith("35" + DateTime.Now.ToString("yyMM") + "12345678000195" + "58" + "001" + "000000050" + "1" + m.RandomCode, m.AccessKey);
        Assert.NotNull(m.IssuedAt);
        Assert.All(m.Ctes, c => Assert.True(c.IsActive));
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Confirm_refuses_without_insurance_and_refreshes_from_branch_when_available()
    {
        var key = await SeedReadyAsync();
        var manifest = await _db.Context.TransportManifests.SingleAsync();
        manifest.InsurerTaxId = null; manifest.InsurerName = null; manifest.PolicyNumber = null; manifest.InsuranceResponsible = null;
        var settings = await _db.Context.BranchTransportSettings.SingleAsync();
        var backup = (settings.InsuranceResponsible, settings.InsurerTaxId, settings.InsurerName, settings.PolicyNumber);
        settings.InsuranceResponsible = null; settings.InsurerTaxId = null; settings.InsurerName = null; settings.PolicyNumber = null;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));
        Assert.Contains(MdfeReadinessValidator.InsuranceRequired, ex.Message);

        (settings.InsuranceResponsible, settings.InsurerTaxId, settings.InsurerName, settings.PolicyNumber) = backup;
        await _db.SaveChangesAsync();
        await Confirm().ConfirmAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Confirmed, m.Status);
        Assert.Equal("11222333000181", m.InsurerTaxId);
    }

    [Fact]
    public async Task Confirm_refuses_when_linked_cte_endorsement_was_cancelled()
    {
        var key = await SeedReadyAsync();
        var cte = await _db.Context.TransportDocuments.SingleAsync();
        cte.EndorsementStatus = CteEndorsementStatus.Cancelled;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("Averbe os CT-e antes de incluir no MDF-e: 7.", ex.Message);
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Confirm_without_ciot_reports_the_message_once()
    {
        var key = await SeedReadyAsync();
        var manifest = await _db.Context.TransportManifests.SingleAsync();
        manifest.CiotNumber = null;
        await _db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains(MdfeReadinessValidator.CiotRequiredWithRntrc, e.Message);
        Assert.Equal(1, e.Message.Split(MdfeReadinessValidator.CiotRequiredWithRntrc).Length - 1);
    }

    [Fact]
    public async Task Confirm_refuses_m3_without_weight()
    {
        var key = await SeedReadyAsync(weight: 0, unit: "03");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains(MdfeReadinessValidator.WeightRequired, e.Message);
        Assert.Equal(0, _numbers.Calls);
        Assert.Null((await LoadAsync(key)).Number);
    }

    [Fact]
    public async Task Confirm_refuses_disabled_branch()
    {
        var key = await SeedReadyAsync(enabled: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("não está habilitada", e.Message);
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Confirm_refuses_non_draft_and_other_erp()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");

        await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));
        await Assert.ThrowsAsync<DefaultException>(() => Confirm("SAPB1").ConfirmAsync(key, "tester"));
    }

    [Fact]
    public async Task Confirm_busy_lock_is_refused()
    {
        var key = await SeedReadyAsync();
        _numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Reopen_keeps_number_and_key()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        var first = await LoadAsync(key);

        await Reopen().ReopenAsync(key);
        Assert.Equal(MdfeStatus.Draft, (await LoadAsync(key)).Status);
        await Confirm().ConfirmAsync(key, "tester");

        var second = await LoadAsync(key);
        Assert.Equal(first.Number, second.Number);
        Assert.Equal(first.AccessKey, second.AccessKey);
        Assert.Equal(first.RandomCode, second.RandomCode);
        Assert.Equal(first.IssuedAt, second.IssuedAt);
        Assert.Equal(1, _numbers.Calls);
    }

    [Fact]
    public async Task Reopen_refuses_draft()
    {
        var key = await SeedReadyAsync();
        await Assert.ThrowsAsync<DefaultException>(() => Reopen().ReopenAsync(key));
    }

    [Fact]
    public async Task Discard_voids_and_frees_ctes()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");

        await Discard().DiscardNumberAsync(key);

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Voided, m.Status);
        Assert.Equal(50, m.Number);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Discard_voids_numbered_draft_and_frees_ctes()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        await Reopen().ReopenAsync(key);

        await Discard().DiscardNumberAsync(key);

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Voided, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task Discard_voids_rejected_and_frees_ctes()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        var tracked = await _db.Context.TransportManifests.Include(x => x.Ctes).SingleAsync(x => x.Key == key);
        tracked.ApplyStatus(MdfeStatus.Rejected);
        await _db.SaveChangesAsync();

        await Discard().DiscardNumberAsync(key);

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Voided, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task Reopen_and_discard_refuse_other_erp()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");

        await Assert.ThrowsAsync<DefaultException>(() => new TransportManifestsReopenService(_db, MdfeTestSupport.Config("SAPB1"), _numbers).ReopenAsync(key));
        await Assert.ThrowsAsync<DefaultException>(() =>
            new TransportManifestsDiscardNumberService(_db, MdfeTestSupport.Config("SAPB1"), _numbers).DiscardNumberAsync(key));
        Assert.Equal(MdfeStatus.Confirmed, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Discard_refuses_draft_without_number()
    {
        var key = await SeedReadyAsync();
        await Assert.ThrowsAsync<DefaultException>(() => Discard().DiscardNumberAsync(key));
    }

    [Fact]
    public async Task Confirm_late_on_last_day_of_month_uses_brasilia_month_for_key_and_issue_date()
    {
        var key = await SeedReadyAsync();
        // 31/10 23:30 em Brasília = 01/11 02:30 UTC (o relógio do servidor Linux).
        var utc = new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero);

        await Confirm(clock: () => utc).ConfirmAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal("2610", m.AccessKey!.Substring(2, 4));
        Assert.Equal(new DateTime(2026, 10, 31, 23, 30, 0), m.IssuedAt);
    }

    [Fact]
    public async Task Reopen_refuses_while_emission_lock_is_held()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        _numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Reopen().ReopenAsync(key));
        _numbers.EmissionBusy = false;
        Assert.Equal(MdfeStatus.Confirmed, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Reopen_takes_emission_lock_and_releases_it()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        var locksDuring = -1;
        _db.Context.SavingChanges += (_, _) => locksDuring = _numbers.LocksHeld;

        await Reopen().ReopenAsync(key);

        Assert.Equal(1, locksDuring);
        Assert.Equal(0, _numbers.LocksHeld);
        Assert.Equal(MdfeStatus.Draft, (await LoadAsync(key)).Status);
    }
}
