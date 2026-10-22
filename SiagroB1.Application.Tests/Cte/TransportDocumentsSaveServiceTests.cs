using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Inclusão/edição do CT-e: validações, rota sugerida, snapshots e avisos.</summary>
public class TransportDocumentsSaveServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly CtePartnerCatalog _partners = new CtePartnerCatalog()
        .Add("REM", "REMETENTE LTDA", "11222333000181", "3509502", "SP")
        .Add("DES", "DESTINATARIO LTDA", "44555666000172", "3550308", "SP")
        .Add("RCB", "RECEBEDOR LTDA", "77888999000163", "4106902", "PR")
        .Add("ISE", "ISENTO LTDA", "11111111000191", "3550308", "SP", "ISENTO")
        .Add("NCT", "PESSOA FISICA", "52998224725", "3550308", "SP", null)
        .Add("SEM", "SEM IBGE", "22222222000191", null, "SP");

    private TransportDocumentsSaveService Service(string erp = "STANDALONE") =>
        new(_db, TaxTestServices.Config(erp), new TransportDocumentDefaults(_db, _partners),
            new TransportDocumentsTaxApplyService(_db, new UsageService(_db, NullLogger<UsageService>.Instance),
                new IbsCbsRatesService(_db)));

    private static TransportDocumentInput Input(params TransportDocumentPartyInput[] extra) => new()
    {
        BranchCode = "01",
        Series = 1,
        TakerRole = 0,
        Parties = [new(CtePartyRole.Sender, "REM"), new(CtePartyRole.Recipient, "DES"), .. extra],
        NfeKeys = [CteTestKeys.Nfe(1)],
        PredominantProduct = "SOJA EM GRAOS",
        CargoValue = 100000m,
        Quantities = [new("01", "PESO BRUTO", 30000m)],
        Components = [new("FRETE PESO", 650m)],
    };

    private async Task<TransportDocument> LoadAsync(Guid key) =>
        await _db.Context.TransportDocuments.AsNoTracking()
            .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities).Include(x => x.Components)
            .SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Saves_costa_case_with_route_suggested_from_receiver()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);

        var result = await Service().SaveAsync(Input(new TransportDocumentPartyInput(CtePartyRole.Receiver, "RCB")), "tester");

        var doc = await LoadAsync(result.Key);
        Assert.Equal("3509502", doc.StartMunicipalityCode);
        Assert.Equal("SP", doc.StartState);
        Assert.Equal("4106902", doc.EndMunicipalityCode);
        Assert.Equal("PR", doc.EndState);
        Assert.Equal(3, doc.Parties.Count);
        Assert.Equal(650m, doc.ServiceTotal);
        Assert.Equal(CteStatus.Draft, doc.Status);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("ISE", 2)]
    [InlineData("NCT", 9)]
    [InlineData("REM", 1)]
    public async Task Taker_ie_indicator_comes_from_partner(string taker, int expected)
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var input = Input(new TransportDocumentPartyInput(CtePartyRole.OtherTaker, taker)) with { TakerRole = 4 };

        var result = await Service().SaveAsync(input, "tester");

        var doc = await LoadAsync(result.Key);
        Assert.Equal(expected, doc.Parties.Single(p => p.Role == CtePartyRole.OtherTaker).IndIE);
    }

    [Fact]
    public async Task Invalid_nfe_key_is_rejected()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var bad = CteTestKeys.Nfe(1)[..43] + (CteTestKeys.Nfe(1)[43] == '0' ? "1" : "0");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { NfeKeys = [bad] }, "tester"));

        Assert.Equal($"Chave de NF-e inválida: {bad}.", ex.Message);
    }

    [Fact]
    public async Task Repeated_key_in_document_is_rejected()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var key = CteTestKeys.Nfe(1);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { NfeKeys = [key, key] }, "tester"));

        Assert.Equal($"A chave {key} aparece mais de uma vez.", ex.Message);
    }

    [Fact]
    public async Task Missing_nfe_key_is_rejected()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { NfeKeys = [] }, "tester"));

        Assert.Equal("Informe ao menos uma NF-e transportada.", ex.Message);
    }

    [Fact]
    public async Task Party_and_component_rules_use_exact_messages()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);

        var noRecipient = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { Parties = [new(CtePartyRole.Sender, "REM")] }, "tester"));
        Assert.Equal("Informe remetente e destinatário.", noRecipient.Message);

        var noTaker = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { TakerRole = 4 }, "tester"));
        Assert.Equal("Informe o tomador do serviço.", noTaker.Message);

        var noIbge = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { Parties = [new(CtePartyRole.Sender, "SEM"), new(CtePartyRole.Recipient, "DES")] }, "tester"));
        Assert.Equal("O endereço de SEM IBGE está sem município (IBGE).", noIbge.Message);

        var noValue = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { Components = [new("FRETE PESO", 0m)] }, "tester"));
        Assert.Equal("Informe o valor da prestação.", noValue.Message);
    }

    [Fact]
    public async Task Duplicate_key_in_other_document_returns_warning()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        await Service().SaveAsync(Input(), "tester");

        var second = await Service().SaveAsync(Input(), "tester");

        var warning = Assert.Single(second.Warnings);
        Assert.Contains(CteTestKeys.Nfe(1), warning);
    }

    [Fact]
    public async Task Key_in_cancelled_document_is_not_warned()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var first = await Service().SaveAsync(Input(), "tester");
        var doc = await _db.Context.TransportDocuments.SingleAsync(x => x.Key == first.Key);
        doc.Status = CteStatus.Cancelled;
        await _db.SaveChangesAsync();

        var second = await Service().SaveAsync(Input(), "tester");

        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task Combination_snapshot_copies_plates_and_default_driver()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        _db.Context.TruckDrivers.Add(new TruckDriver { Code = "52998224725", Name = "JOAO MOTORISTA", Document = "52998224725", Phone = "" });
        _db.Context.Trucks.AddRange(
            new Truck { Code = "ABC1D23", Model = "CAVALO", Renavam = "12345678901", TareWeight = 9000, Kind = TruckKind.Traction, StateKey = "SP" },
            new Truck { Code = "REB1A11", Model = "CARRETA 1", Kind = TruckKind.Trailer, StateKey = "SP" },
            new Truck { Code = "REB2B22", Model = "CARRETA 2", Kind = TruckKind.Trailer, StateKey = "SP" });
        _db.Context.TruckCombinations.Add(new TruckCombination
        {
            Code = "RODOTREM", Description = "RODOTREM", TractionTruckCode = "ABC1D23", DefaultDriverCode = "52998224725",
            Trailers =
            [
                new TruckCombinationTrailer { CombinationCode = "RODOTREM", Position = 2, TrailerTruckCode = "REB2B22" },
                new TruckCombinationTrailer { CombinationCode = "RODOTREM", Position = 1, TrailerTruckCode = "REB1A11" },
            ],
        });
        await _db.SaveChangesAsync();

        var result = await Service().SaveAsync(Input() with { TruckCombinationCode = "RODOTREM" }, "tester");

        var doc = await LoadAsync(result.Key);
        Assert.Equal("ABC1D23", doc.TractionPlate);
        Assert.Equal("REB1A11,REB2B22", doc.TrailerPlates);
        Assert.Equal("52998224725", doc.DriverCpf);
        Assert.Equal("JOAO MOTORISTA", doc.DriverName);
        Assert.Contains("12345678901", doc.VehicleSnapshotJson);
    }

    [Fact]
    public async Task Insurance_defaults_from_branch_transport_settings()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        _db.Context.BranchTransportSettings.Add(new BranchTransportSettings
        {
            BranchCode = "01", CompanyRntrc = "11382119", InsuranceResponsible = InsuranceResponsible.Issuer,
            InsurerTaxId = "33164021000100", InsurerName = "SEGURADORA", PolicyNumber = "APOL-1",
        });
        await _db.SaveChangesAsync();

        var result = await Service().SaveAsync(Input(), "tester");

        var doc = await LoadAsync(result.Key);
        Assert.Equal("1", doc.InsuranceResponsible);
        Assert.Equal("33164021000100", doc.InsurerTaxId);
        Assert.Equal("SEGURADORA", doc.InsurerName);
        Assert.Equal("APOL-1", doc.PolicyNumber);
        Assert.Equal("11382119", doc.Rntrc);
    }

    [Fact]
    public async Task Rejected_document_cannot_change_series()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var saved = await Service().SaveAsync(Input(), "tester");
        var doc = await _db.Context.TransportDocuments.SingleAsync(x => x.Key == saved.Key);
        doc.Status = CteStatus.Rejected;
        doc.Number = 10;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { Key = saved.Key, Series = 2 }, "tester"));

        Assert.Equal(TransportDocumentsSaveService.RejectedIdentityLocked, ex.Message);
    }

    [Fact]
    public async Task Confirmed_document_requires_reopen_to_edit()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var saved = await Service().SaveAsync(Input(), "tester");
        var doc = await _db.Context.TransportDocuments.SingleAsync(x => x.Key == saved.Key);
        doc.Status = CteStatus.Confirmed;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SaveAsync(Input() with { Key = saved.Key }, "tester"));

        Assert.Equal(TransportDocumentsSaveService.ConfirmedRequiresReopen, ex.Message);
    }

    [Fact]
    public async Task Editing_draft_replaces_children()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var saved = await Service().SaveAsync(Input(), "tester");

        var edited = await Service().SaveAsync(Input() with
        {
            Key = saved.Key, NfeKeys = [CteTestKeys.Nfe(2), CteTestKeys.Nfe(3)], Components = [new("FRETE PESO", 700m), new("PEDAGIO", 50m)],
        }, "tester");

        var doc = await LoadAsync(edited.Key);
        Assert.Equal(saved.Key, edited.Key);
        Assert.Equal(2, doc.NfeKeys.Count);
        Assert.Equal(2, doc.Parties.Count);
        Assert.Equal(750m, doc.ServiceTotal);
        Assert.Equal(1, await _db.Context.TransportDocuments.CountAsync());
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").SaveAsync(Input(), "tester"));

        Assert.Equal("O CT-e só existe no modo STANDALONE.", ex.Message);
    }
}
