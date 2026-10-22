using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Confirmar / Reabrir / Descartar número do CT-e: número e chave nascem uma vez e não mudam.</summary>
public class TransportDocumentsConfirmServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeCteNumberReservationService _numbers = new(100);

    private TransportDocumentsConfirmService Confirm(string erp = "STANDALONE") =>
        new(_db, TaxTestServices.Config(erp), _numbers, new CteReadinessValidator(_db));

    private TransportDocumentsReopenService Reopen() => new(_db, TaxTestServices.Config("STANDALONE"));

    private TransportDocumentsDiscardNumberService Discard() => new(_db, TaxTestServices.Config("STANDALONE"), _numbers);

    private async Task<Guid> SeedReadyAsync(
        bool enabled = true, string driverCpf = "52998224725", int? tare = 9000, int? capacity = 30000,
        DateTime? certificateUntil = null, string? rntrc = "12345678", int? usage = 5, string? cfop = "6353")
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var settings = await _db.Context.BranchCteSettings.SingleAsync();
        settings.Enabled = enabled;
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01", CertificatePfx = [1], CertificateValidUntil = certificateUntil ?? DateTime.Now.AddMonths(6),
        });

        var snapshot = JsonSerializer.Serialize(new object[]
        {
            new { Plate = "ABC1D23", Kind = (int)TruckKind.Traction, TareWeight = tare, CapacityKg = capacity, WheelType = (int?)TruckWheelType.TractorUnit },
            new { Plate = "REB1A11", Kind = (int)TruckKind.Trailer, TareWeight = (int?)7000, CapacityKg = (int?)28000, WheelType = (int?)null },
        });
        var doc = new TransportDocument
        {
            BranchCode = "01", Series = 1, UsageCode = usage, Cfop = cfop, Rntrc = rntrc,
            TractionPlate = "ABC1D23", TrailerPlates = "REB1A11", VehicleSnapshotJson = snapshot, DriverCpf = driverCpf,
        };
        _db.Context.TransportDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return doc.Key;
    }

    private Task<TransportDocument> LoadAsync(Guid key) =>
        _db.Context.TransportDocuments.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Confirm_reserves_number_and_builds_key()
    {
        var key = await SeedReadyAsync();

        await Confirm().ConfirmAsync(key, "tester");

        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Confirmed, doc.Status);
        Assert.Equal(100, doc.Number);
        Assert.Equal(8, doc.RandomCode!.Length);
        Assert.NotEqual("00000100", doc.RandomCode);
        Assert.Equal(44, doc.AccessKey!.Length);
        Assert.StartsWith("35" + DateTime.Now.ToString("yyMM") + "12345678000195" + "57" + "001" + "000000100" + "1" + doc.RandomCode, doc.AccessKey);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Confirm_twice_keeps_same_number_and_key()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        var first = await LoadAsync(key);

        await Reopen().ReopenAsync(key);
        Assert.Equal(CteStatus.Draft, (await LoadAsync(key)).Status);
        await Confirm().ConfirmAsync(key, "tester");

        var second = await LoadAsync(key);
        Assert.Equal(first.Number, second.Number);
        Assert.Equal(first.AccessKey, second.AccessKey);
        Assert.Equal(first.RandomCode, second.RandomCode);
        Assert.Equal(1, _numbers.Calls);
    }

    [Fact]
    public async Task Confirm_without_tare_lists_vehicle()
    {
        var key = await SeedReadyAsync(tare: null);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("Veículo ABC1D23 sem tara/capacidade: complete o cadastro.", e.Message);
        Assert.Equal(0, _numbers.Calls);
        Assert.Null((await LoadAsync(key)).Number);
    }

    [Fact]
    public async Task Confirm_with_invalid_driver_cpf_is_rejected()
    {
        var key = await SeedReadyAsync(driverCpf: "11111111111");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("CPF do motorista", e.Message);
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Confirm_disabled_branch_is_rejected()
    {
        var key = await SeedReadyAsync(enabled: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("não está habilitada", e.Message);
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Confirm_without_nature_is_rejected()
    {
        var key = await SeedReadyAsync(usage: null, cfop: null);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("natureza", e.Message);
    }

    [Fact]
    public async Task Confirm_with_expired_certificate_or_missing_rntrc_is_rejected()
    {
        var key = await SeedReadyAsync(certificateUntil: DateTime.Now.AddDays(-1), rntrc: null);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));

        Assert.Contains("certificado", e.Message);
        Assert.Contains("RNTRC", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_sapb1_and_non_draft()
    {
        var key = await SeedReadyAsync();
        await Assert.ThrowsAsync<DefaultException>(() => Confirm("SAPB1").ConfirmAsync(key, "tester"));

        await Confirm().ConfirmAsync(key, "tester");
        await Assert.ThrowsAsync<DefaultException>(() => Confirm().ConfirmAsync(key, "tester"));
    }

    [Fact]
    public async Task Discard_number_moves_it_to_void_list()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");

        await Discard().DiscardNumberAsync(key, "Mudou o mês de emissão");

        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Draft, doc.Status);
        Assert.Null(doc.Number);
        Assert.Null(doc.AccessKey);
        Assert.Null(doc.RandomCode);
        var voided = await _db.Context.CteNumbersToVoid.SingleAsync();
        Assert.Equal((100, 1, "01"), (voided.Number, voided.Series, voided.BranchCode));
        Assert.Equal("Mudou o mês de emissão", voided.Reason);

        // Nova confirmação pega outro número.
        await Confirm().ConfirmAsync(key, "tester");
        Assert.Equal(101, (await LoadAsync(key)).Number);
    }

    [Fact]
    public async Task Discard_requires_number_reason_and_allowed_status()
    {
        var key = await SeedReadyAsync();
        await Assert.ThrowsAsync<DefaultException>(() => Discard().DiscardNumberAsync(key, "motivo"));

        await Confirm().ConfirmAsync(key, "tester");
        await Assert.ThrowsAsync<DefaultException>(() => Discard().DiscardNumberAsync(key, "  "));

        var doc = await _db.Context.TransportDocuments.SingleAsync();
        doc.Status = CteStatus.Authorized;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<DefaultException>(() => Discard().DiscardNumberAsync(key, "motivo"));
    }

    [Fact]
    public async Task Discard_refuses_while_emission_lock_is_held()
    {
        var key = await SeedReadyAsync();
        await Confirm().ConfirmAsync(key, "tester");
        _numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Discard().DiscardNumberAsync(key, "motivo"));

        Assert.Empty(await _db.Context.CteNumbersToVoid.ToListAsync());
        Assert.Equal(100, (await LoadAsync(key)).Number);
    }

    [Fact]
    public async Task Confirm_uses_municipality_state_for_uf_code_before_branch_state()
    {
        var key = await SeedReadyAsync();
        var branch = await _db.Context.Branchs.SingleAsync();
        branch.StateCode = "XX";
        branch.MunicipalityCode = "4106902"; // Curitiba/PR
        await _db.SaveChangesAsync();

        await Confirm().ConfirmAsync(key, "tester");

        Assert.StartsWith("41", (await LoadAsync(key)).AccessKey);
    }
}
