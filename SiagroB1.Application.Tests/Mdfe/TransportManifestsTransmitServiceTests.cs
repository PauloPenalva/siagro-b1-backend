using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Transmitir/consultar o MDF-e com a SEFAZ simulada: XML assinado gravado antes, recuperação por consulta.</summary>
public class TransportManifestsTransmitServiceTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(50);
    private readonly FakeMdfeSefazClient _sefaz = new();
    private DateTimeOffset _now = October;

    private TransportManifestsTransmitService Transmit()
    {
        var config = NfeTestSeed.Config();
        return new TransportManifestsTransmitService(
            _db, config, _numbers, new MdfeServiceContextFactory(_db, new NfeOptions(config)), _sefaz, new NfeOptions(config),
            new MdfeReadinessValidator(_db), () => _now);
    }

    private TransportManifestsConsultService Consult()
    {
        var config = NfeTestSeed.Config();
        return new TransportManifestsConsultService(_db, config, _numbers, new MdfeServiceContextFactory(_db, new NfeOptions(config)), _sefaz);
    }

    private async Task<Guid> SeedConfirmedAsync()
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSPORTADORA", LegalName = "CEAGUI CEREAIS LTDA", TaxId = "12345678000195",
            StateCode = "SP", StateRegistration = "387000000110", TaxRegime = TaxRegime.Normal,
            Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL", MunicipalityCode = "3521705",
            ZipCode = "18440000",
        });
        _db.Context.Municipalities.AddRange(
            new Municipality { Code = "3521705", Name = "ITABERA", StateAbbreviation = "SP" },
            new Municipality { Code = "3550308", Name = "SAO PAULO", StateAbbreviation = "SP" });
        _db.Context.BranchMdfeSettings.Add(new BranchMdfeSettings { BranchCode = "01", Enabled = true });
        _db.Context.BranchTransportSettings.Add(new BranchTransportSettings { BranchCode = "01", CompanyRntrc = "12345678" });
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01",
            CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });

        var issuedAt = October.DateTime;
        var m = new TransportManifest
        {
            BranchCode = "01", Status = MdfeStatus.Confirmed, Series = 1, Number = 50, RandomCode = "12345678", IssuedAt = issuedAt,
            AccessKey = MdfeAccessKey.Build("35", issuedAt, "12345678000195", 1, 50, 1, "12345678"),
            Environment = NfeEnvironment.Homologation, UfStart = "SP", UfEnd = "SP", TractionPlate = "ABC1D23",
            DriverCpf = "52998224725", DriverName = "JOAO DA SILVA", CargoType = "01", ProductDescription = "SOJA EM GRAOS",
            LoadingZip = "18440000", UnloadingZip = "01001000", CteCount = 1, CargoValue = 60000m, WeightUnit = "01", GrossWeight = 30000m,
            InsuranceResponsible = "1", InsurerTaxId = "11222333000181", InsurerName = "SEGURADORA", PolicyNumber = "APOL1",
            Ncm = "12019000", CiotNumber = "123456789012", CiotResponsibleTaxId = "11222333000181",
        };
        m.Payments.Add(new TransportManifestPayment { ContractorTaxId = "52998224725", ContractorName = "TRANSPORTADOR", Total = 1, Pix = "frete@transp.com.br", Components = [new TransportManifestPaymentComponent { Type = "99", Value = 1, Description = "FRETE" }] });
        m.LoadingCities.Add(new TransportManifestLoadingCity { LineNum = 1, CityIbge = "3521705", CityName = "ITABERA" });
        m.Ctes.Add(new TransportManifestCte
        {
            LineNum = 1, TransportDocumentKey = Guid.NewGuid(), UnloadingCityIbge = "3550308", UnloadingCityName = "SAO PAULO",
            CteAccessKey = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 1, 7, 1, "11111111"),
            Endorsements = "AV1",
        });
        m.Vehicles.Add(new TransportManifestVehicle
        {
            LineNum = 1, IsTraction = true, Plate = "ABC1D23", Renavam = "12345678901", TareKg = 9000, CapacityKg = 30000,
            BodyType = "03", WheelType = "03", Uf = "SP",
        });
        _db.Context.TransportManifests.Add(m);
        await _db.SaveChangesAsync();
        return m.Key;
    }

    private Task<TransportManifest> LoadAsync(Guid key) =>
        _db.Context.TransportManifests.AsNoTracking().Include(x => x.Xmls).Include(x => x.Ctes).SingleAsync(x => x.Key == key);

    /// <summary>Protocolo cujo digVal é o do XML assinado (como a SEFAZ responde de verdade).</summary>
    private static NfeSefazResult Ok(SignedMdfe sent)
    {
        var auth = FakeMdfeSefazClient.Authorized(sent.AccessKey);
        return auth with { ProtocolXml = auth.ProtocolXml!.Replace("TESTE=", NfeProcComposer.SignedDigest(sent.Xml)) };
    }

    private Func<string, NfeSefazResult> OkForSent(int index) => _ => Ok(_sefaz.Sent[index]);

    private static readonly Func<string, NfeSefazResult> NotFound = _ => new NfeSefazResult(217, "MDF-e não consta na base");

    [Fact]
    public async Task Authorized_stores_protocol_and_xml()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(100, result.StatusCode);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.Equal(FakeMdfeSefazClient.AuthorizationProtocol, m.ProtocolNumber);
        Assert.NotNull(m.AuthorizedAt);
        Assert.Equal(_sefaz.Sent[0].Xml, m.Xmls.Single(x => x.Kind == MdfeXmlKind.Signed).Xml);
        var proc = m.Xmls.Single(x => x.Kind == MdfeXmlKind.Authorized).Xml;
        Assert.Contains("<mdfeProc versao=\"3.00\"", proc);
        Assert.Contains("<protMDFe", proc);
        Assert.All(m.Ctes, c => Assert.True(c.IsActive));
        Assert.Equal(0, _numbers.LocksHeld);
        Assert.Equal(m.AccessKey, _sefaz.Sent[0].AccessKey);
    }

    [Fact]
    public async Task Signed_xml_is_persisted_before_send()
    {
        var key = await SeedConfirmedAsync();
        (MdfeStatus Status, int Signed)? atSend = null;
        _sefaz.BeforeAuthorize = async () =>
        {
            var m = await LoadAsync(key);
            atSend = (m.Status, m.Xmls.Count(x => x.Kind == MdfeXmlKind.Signed));
        };
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal((MdfeStatus.Processing, 1), atSend);
    }

    [Fact]
    public async Task Timeout_recovers_by_consult_without_resending()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(FakeMdfeSefazClient.NoResponse);
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(100, result.StatusCode);
        Assert.Equal(1, _sefaz.AuthorizeCalls);
        Assert.Equal(1, _sefaz.ConsultCalls);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Timeout_then_not_found_stays_processing()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(FakeMdfeSefazClient.NoResponse);
        _sefaz.ConsultResponses.Enqueue(NotFound);

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(TransportManifestsTransmitService.NoResponse, result.Reason);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Processing, m.Status);
        Assert.Equal(TransportManifestsTransmitService.NoResponse, m.RejectionReason);
    }

    [Fact]
    public async Task Retransmit_after_timeout_consults_first()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(FakeMdfeSefazClient.NoResponse);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");
        var issuedAt = (await LoadAsync(key)).IssuedAt;

        // A SEFAZ autorizou o primeiro envio: a nova transmissão só consulta.
        _now = October.AddMinutes(30);
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));
        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(1, _sefaz.AuthorizeCalls);
        Assert.Equal(2, _sefaz.ConsultCalls);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.Equal(issuedAt, m.IssuedAt);
    }

    [Fact]
    public async Task Retransmit_after_timeout_not_found_resends_same_xml()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(FakeMdfeSefazClient.NoResponse);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");

        _now = October.AddMinutes(30);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(_sefaz.Sent[0].Xml, _sefaz.Sent[1].Xml);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Duplicate_539_recovers_by_consult()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => new NfeSefazResult(539, "Duplicidade de MDF-e"));
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));

        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
        Assert.Equal(1, _sefaz.AuthorizeCalls);
    }

    [Fact]
    public async Task Duplicate_539_with_other_key_is_rejected_with_discard_hint()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => new NfeSefazResult(539, "Duplicidade de MDF-e com diferença na chave"));
        _sefaz.ConsultResponses.Enqueue(NotFound);

        await Transmit().TransmitAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Rejected, m.Status);
        Assert.Equal(TransportManifestsTransmitService.BurnedNumberReason, m.RejectionReason);
    }

    [Fact]
    public async Task Rejected_without_insurance_refreshes_it_from_branch_before_retransmit()
    {
        var key = await SeedConfirmedAsync();
        var seeded = await _db.Context.TransportManifests.SingleAsync();
        seeded.InsurerTaxId = null; seeded.InsurerName = null; seeded.PolicyNumber = null; seeded.InsuranceResponsible = null;
        seeded.ApplyStatus(MdfeStatus.Rejected);
        await _db.SaveChangesAsync();

        // Filial ainda sem seguro: bloqueia antes de assinar/enviar.
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));
        Assert.Equal(MdfeReadinessValidator.InsuranceRequired, ex.Message);
        Assert.Empty(_sefaz.Sent);

        var settings = await _db.Context.BranchTransportSettings.SingleAsync();
        settings.InsuranceResponsible = InsuranceResponsible.Issuer; settings.InsurerTaxId = "11222333000181";
        settings.InsurerName = "SEGURADORA"; settings.PolicyNumber = "APOL9";
        await _db.SaveChangesAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.Equal("APOL9", m.PolicyNumber);
        Assert.Contains("<seg>", _sefaz.Sent[0].Xml);
    }

    [Fact]
    public async Task Rejected_then_retransmit_keeps_number_and_key()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => FakeMdfeSefazClient.Rejected());

        await Transmit().TransmitAsync(key, "tester");

        var rejected = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Rejected, rejected.Status);
        Assert.Equal("611", rejected.RejectionCode);
        Assert.Equal("Rejeição: Existe MDF-e não encerrado", rejected.RejectionReason);

        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.Equal(50, m.Number);
        Assert.Equal(rejected.AccessKey, m.AccessKey);
        Assert.All(_sefaz.Sent, s => Assert.Equal(rejected.AccessKey, s.AccessKey));
        Assert.Equal(0, _numbers.Calls);
        Assert.Null(m.RejectionCode);
    }

    [Fact]
    public async Task Issue_date_renewed_only_after_processed_rejection()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => FakeMdfeSefazClient.Rejected());
        await Transmit().TransmitAsync(key, "tester");

        _now = October.AddMinutes(30);
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(October.AddMinutes(30).DateTime, m.IssuedAt);
        Assert.NotEqual(_sefaz.Sent[0].Xml, _sefaz.Sent[1].Xml);
        Assert.Equal(_sefaz.Sent[1].Xml, m.Xmls.Single(x => x.Kind == MdfeXmlKind.Signed).Xml);
    }

    [Fact]
    public async Task Different_month_is_refused_with_discard_hint()
    {
        var key = await SeedConfirmedAsync();
        _now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-3));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));

        Assert.Equal("A chave foi gerada em 10/2026 e a emissão cairia em outro mês. Descarte o número e confirme de novo.", e.Message);
        Assert.Equal(0, _sefaz.AuthorizeCalls);
        Assert.Equal(MdfeStatus.Confirmed, (await LoadAsync(key)).Status);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    public async Task Mdfe_has_no_denial_any_processed_non_authorized_answer_is_rejection(int code)
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => new NfeSefazResult(code, "Rejeição"));

        await Transmit().TransmitAsync(key, "tester");

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Rejected, m.Status);
        Assert.Equal(code.ToString(), m.RejectionCode);
        Assert.All(m.Ctes, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task Draft_cannot_be_transmitted()
    {
        var key = await SeedConfirmedAsync();
        var m = await _db.Context.TransportManifests.Include(x => x.Ctes).SingleAsync();
        m.ApplyStatus(MdfeStatus.Draft);
        await _db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));
        Assert.Equal(TransportManifestsTransmitService.NotTransmittable, e.Message);
        Assert.Equal(0, _sefaz.AuthorizeCalls);
    }

    [Fact]
    public async Task Consult_recovers_processing_authorization()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(FakeMdfeSefazClient.NoResponse);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");

        _sefaz.ConsultResponses.Enqueue(OkForSent(0));
        await Consult().ConsultAsync(key);

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.Single(m.Xmls, x => x.Kind == MdfeXmlKind.Authorized);
    }

    [Fact]
    public async Task Consult_marks_cancelled_and_closed_on_sefaz()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        _sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(132, "Encerramento de MDF-e homologado"));
        await Consult().ConsultAsync(key);
        var closed = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Closed, closed.Status);
        Assert.All(closed.Ctes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task Consult_marks_cancelled()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        _sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(101, "Cancelamento de MDF-e homologado"));
        await Consult().ConsultAsync(key);

        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Cancelled, m.Status);
        Assert.NotNull(m.CancelledAt);
    }

    [Fact]
    public async Task Rejected_is_revalidated_before_resigning_when_endorsement_was_cancelled()
    {
        var key = await SeedConfirmedAsync();
        var tracked = await _db.Context.TransportManifests.Include(x => x.Ctes).SingleAsync(x => x.Key == key);
        tracked.ApplyStatus(MdfeStatus.Rejected);
        var cte = await MdfeTestSupport.AddCteAsync(_db, 7);
        cte.EndorsementStatus = CteEndorsementStatus.Cancelled;
        tracked.Ctes.Single().TransportDocumentKey = cte.Key;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));

        Assert.Contains("Averbe os CT-e", ex.Message);
        Assert.Empty(_sefaz.Sent);
        Assert.Equal(MdfeStatus.Rejected, (await LoadAsync(key)).Status);
    }
}
