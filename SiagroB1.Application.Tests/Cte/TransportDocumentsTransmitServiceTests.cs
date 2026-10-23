using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Transmitir/consultar o CT-e com a SEFAZ simulada: recuperação por consulta, sem reenvio.</summary>
public class TransportDocumentsTransmitServiceTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(-3));

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeCteNumberReservationService _numbers = new(100);
    private readonly FakeCteSefazClient _sefaz = new();
    private DateTimeOffset _now = October;

    private TransportDocumentsTransmitService Transmit()
    {
        var config = NfeTestSeed.Config();
        return new TransportDocumentsTransmitService(
            _db, config, _numbers, new CteServiceContextFactory(_db, new NfeOptions(config)), _sefaz, new NfeOptions(config), () => _now);
    }

    private TransportDocumentsConsultService Consult()
    {
        var config = NfeTestSeed.Config();
        return new TransportDocumentsConsultService(
            _db, config, _numbers, new CteServiceContextFactory(_db, new NfeOptions(config)), _sefaz);
    }

    private async Task<Guid> SeedConfirmedAsync(NfeEnvironment environment = NfeEnvironment.Homologation)
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSPORTADORA", LegalName = "CEAGUI CEREAIS LTDA", TaxId = "12345678000195",
            StateCode = "SP", StateRegistration = "387000000110", TaxRegime = TaxRegime.Normal, IssuesNfe = true,
            Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL", MunicipalityCode = "3521705",
            ZipCode = "18440000",
        });
        _db.Context.Municipalities.AddRange(
            new Municipality { Code = "3521705", Name = "ITABERA", StateAbbreviation = "SP" },
            new Municipality { Code = "3550308", Name = "SAO PAULO", StateAbbreviation = "SP" });
        _db.Context.BranchCteSettings.Add(new BranchCteSettings { BranchCode = "01", Enabled = true, Environment = environment });
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01",
            CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });

        var issuedAt = October.DateTime;
        var doc = new TransportDocument
        {
            BranchCode = "01", Series = 1, Number = 100, RandomCode = "12345678", IssuedAt = issuedAt,
            AccessKey = CteAccessKey.Build("35", issuedAt, "12345678000195", 1, 100, 1, "12345678"),
            Status = CteStatus.Confirmed, Environment = environment,
            UsageCode = 5, Cfop = "5352", NatureText = "PRESTACAO DE SERVICO DE TRANSPORTE", TakerRole = 3,
            StartMunicipalityCode = "3521705", StartState = "SP", EndMunicipalityCode = "3550308", EndState = "SP",
            PredominantProduct = "SOJA EM GRAOS", CargoValue = 60000m, ServiceTotal = 1000m, AmountToReceive = 1000m,
            IcmsCst = "00", IcmsBase = 1000m, IcmsRate = 12m, IcmsValue = 120m,
            Rntrc = "12345678", TractionPlate = "ABC1D23", TrailerPlates = "REB1A11,REB2B22",
            DriverCpf = "52998224725", DriverName = "JOAO DA SILVA", Tracked = true, OwnTransport = false,
            Observation = "FRETE CONFORME CONTRATO",
            Parties =
            [
                Party(CtePartyRole.Sender, "11222333000181", "COSTA PRODUTOR RURAL LTDA", "3521705", "ITABERA"),
                Party(CtePartyRole.Recipient, "33444555000122", "ARMAZEM DESTINO LTDA", "3550308", "SAO PAULO"),
            ],
            NfeKeys = [new TransportDocumentNfeKey { AccessKey = CteTestKeys.Nfe(1), Order = 0 }],
            Quantities = [new TransportDocumentQuantity { UnitCode = "01", MeasureType = "PESO BRUTO", Quantity = 30000m }],
            Components = [new TransportDocumentComponent { Name = "FRETE PESO", Value = 1000m }],
        };
        _db.Context.TransportDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return doc.Key;
    }

    private static TransportDocumentParty Party(CtePartyRole role, string taxId, string name, string municipality, string city) => new()
    {
        Role = role, TaxId = taxId, Name = name, StateRegistration = "111222333444", IndIE = 1,
        Street = "RUA A", Number = "1", Neighborhood = "CENTRO", MunicipalityCode = municipality, MunicipalityName = city,
        State = "SP", ZipCode = "01001000",
    };

    private Task<TransportDocument> LoadAsync(Guid key) =>
        _db.Context.TransportDocuments.AsNoTracking().Include(x => x.Xmls).SingleAsync(x => x.Key == key);

    /// <summary>Protocolo de autorização cujo digVal é o do XML assinado (como a SEFAZ responde de verdade).</summary>
    private static NfeSefazResult Ok(SignedCte sent)
    {
        var auth = FakeCteSefazClient.Authorized(sent.AccessKey);
        return auth with { ProtocolXml = auth.ProtocolXml!.Replace("TESTE=", NfeProcComposer.SignedDigest(sent.Xml)) };
    }

    private Func<string, NfeSefazResult> OkForSent(int index) => _ => Ok(_sefaz.Sent[index]);

    private static readonly Func<string, NfeSefazResult> NotFound = _ => new NfeSefazResult(217, "CT-e não consta na base");

    private static void Timeout(FakeCteSefazClient s) => s.AuthorizeResponses.Enqueue(FakeCteSefazClient.NoResponse);

    [Fact]
    public async Task Authorized_stores_protocol_and_xml()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(100, result.StatusCode);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.Equal(FakeCteSefazClient.AuthorizationProtocol, doc.Protocol);
        Assert.NotNull(doc.AuthorizedAt);
        Assert.Equal(_sefaz.Sent[0].Xml, doc.Xmls.Single(x => x.Kind == CteXmlKind.Signed).Xml);
        var xml = doc.Xmls.Single(x => x.Kind == CteXmlKind.Authorized);
        Assert.StartsWith("<?xml", xml.Xml);
        Assert.Contains("<cteProc versao=\"4.00\"", xml.Xml);
        Assert.Contains("<protCTe", xml.Xml);
        Assert.Contains("<qrCodCTe>", xml.Xml);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Signed_xml_is_stored_with_processing_before_sefaz_call()
    {
        var key = await SeedConfirmedAsync();
        (CteStatus Status, int Signed)? atSend = null;
        _sefaz.BeforeAuthorize = async () =>
        {
            var d = await LoadAsync(key);
            atSend = (d.Status, d.Xmls.Count(x => x.Kind == CteXmlKind.Signed));
        };
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal((CteStatus.Processing, 1), atSend);
    }

    [Fact]
    public async Task Rejected_then_retransmit_keeps_number_and_key()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => FakeCteSefazClient.Rejected());

        await Transmit().TransmitAsync(key, "tester");

        var rejected = await LoadAsync(key);
        Assert.Equal(CteStatus.Rejected, rejected.Status);
        Assert.Equal("209", rejected.StatusCode);
        Assert.Equal("Rejeição: IE do emitente inválida", rejected.StatusReason);

        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.Equal(100, doc.Number);
        Assert.Equal(rejected.AccessKey, doc.AccessKey);
        Assert.Equal(rejected.RandomCode, doc.RandomCode);
        Assert.All(_sefaz.Sent, s => Assert.Equal(rejected.AccessKey, s.AccessKey));
        Assert.Equal(0, _numbers.Calls);
    }

    [Fact]
    public async Task Rejected_by_sefaz_then_retransmit_renews_issued_at()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => FakeCteSefazClient.Rejected());
        await Transmit().TransmitAsync(key, "tester");
        var first = (await LoadAsync(key)).IssuedAt;

        _now = October.AddMinutes(30);
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.NotEqual(first, doc.IssuedAt);
        Assert.Equal(October.AddMinutes(30).DateTime, doc.IssuedAt);
        Assert.NotEqual(_sefaz.Sent[0].Xml, _sefaz.Sent[1].Xml);
        Assert.Equal(_sefaz.Sent[1].Xml, doc.Xmls.Single(x => x.Kind == CteXmlKind.Signed).Xml);
    }

    [Fact]
    public async Task Timeout_recovers_authorization_by_consult_without_resending()
    {
        var key = await SeedConfirmedAsync();
        Timeout(_sefaz);
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(100, result.StatusCode);
        Assert.Equal(1, _sefaz.AuthorizeCalls);
        Assert.Equal(1, _sefaz.ConsultCalls);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.Single(doc.Xmls, x => x.Kind == CteXmlKind.Authorized);
    }

    [Fact]
    public async Task Timeout_then_consult_not_found_stays_processing()
    {
        var key = await SeedConfirmedAsync();
        Timeout(_sefaz);
        _sefaz.ConsultResponses.Enqueue(NotFound);

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(TransportDocumentsTransmitService.NoResponse, result.Reason);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Processing, doc.Status);
        Assert.Null(doc.StatusCode);
        Assert.Equal(TransportDocumentsTransmitService.NoResponse, doc.StatusReason);
    }

    [Fact]
    public async Task Retransmit_processing_consults_first_and_resends_same_xml()
    {
        var key = await SeedConfirmedAsync();
        Timeout(_sefaz);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");
        var issuedAt = (await LoadAsync(key)).IssuedAt;

        _now = October.AddMinutes(30);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        _sefaz.AuthorizeResponses.Enqueue(Ok);
        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(2, _sefaz.ConsultCalls);
        Assert.Equal(2, _sefaz.AuthorizeCalls);
        Assert.Equal(_sefaz.Sent[0].Xml, _sefaz.Sent[1].Xml);
        var doc = await LoadAsync(key);
        Assert.Equal(issuedAt, doc.IssuedAt);
        Assert.Equal(CteStatus.Authorized, doc.Status);
    }

    [Fact]
    public async Task Timeout_retransmit_after_late_authorization_uses_original_xml()
    {
        // Envio 1 some no tempo esgotado, mas a SEFAZ autorizou; a consulta ainda não enxerga (217).
        var key = await SeedConfirmedAsync();
        Timeout(_sefaz);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");
        var firstXml = _sefaz.Sent[0].Xml;

        // Nova transmissão: a consulta agora acha a autorização do envio 1 — nada é reenviado.
        _now = October.AddMinutes(30);
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));
        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(1, _sefaz.AuthorizeCalls);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        var proc = doc.Xmls.Single(x => x.Kind == CteXmlKind.Authorized).Xml;
        Assert.Equal(NfeProcComposer.SignedDigest(firstXml), NfeProcComposer.SignedDigest(proc));
        Assert.Equal(NfeProcComposer.SignedDigest(firstXml), NfeProcComposer.ProtocolDigest(proc));
    }

    [Fact]
    public async Task Digest_mismatch_does_not_authorize()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(c => FakeCteSefazClient.Authorized(c.AccessKey)); // digVal "TESTE="

        var result = await Transmit().TransmitAsync(key, "tester");

        Assert.Equal("Protocolo da SEFAZ não confere com o XML enviado: verifique.", result.Reason);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Processing, doc.Status);
        Assert.Equal("Protocolo da SEFAZ não confere com o XML enviado: verifique.", doc.StatusReason);
        Assert.DoesNotContain(doc.Xmls, x => x.Kind == CteXmlKind.Authorized);
    }

    [Fact]
    public async Task Duplicate_539_recovers_by_consult()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(_ => new NfeSefazResult(539, "Duplicidade de CT-e"));
        _sefaz.ConsultResponses.Enqueue(OkForSent(0));

        await Transmit().TransmitAsync(key, "tester");

        Assert.Equal(CteStatus.Authorized, (await LoadAsync(key)).Status);
        Assert.Equal(1, _sefaz.AuthorizeCalls);
    }

    [Fact]
    public async Task Different_month_is_refused_with_discard_hint()
    {
        var key = await SeedConfirmedAsync();
        _now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-3));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));

        Assert.Equal("A chave foi gerada em 10/2026 e a emissão cairia em outro mês. Descarte o número e confirme de novo.", e.Message);
        Assert.Equal(0, _sefaz.AuthorizeCalls);
        Assert.Equal(CteStatus.Confirmed, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Draft_cannot_be_transmitted()
    {
        var key = await SeedConfirmedAsync();
        var doc = await _db.Context.TransportDocuments.SingleAsync();
        doc.Status = CteStatus.Draft;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Transmit().TransmitAsync(key, "tester"));
        Assert.Equal(0, _sefaz.AuthorizeCalls);
    }

    [Fact]
    public async Task Xml_carries_obscont_plate_and_driver()
    {
        var key = await SeedConfirmedAsync();
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        var xml = Assert.Single(_sefaz.Sent).Xml;
        Assert.Contains("<ObsCont xCampo=\"PLACA\"><xTexto>ABC1D23, REB1A11, REB2B22</xTexto></ObsCont>", xml);
        Assert.Contains("<ObsCont xCampo=\"CPFMOTORISTA\"><xTexto>52998224725</xTexto></ObsCont>", xml);
        Assert.Contains("<ObsCont xCampo=\"NOMEMOTORISTA\"><xTexto>JOAO DA SILVA</xTexto></ObsCont>", xml);
        Assert.Contains("<ObsCont xCampo=\"RASTREADO\"><xTexto>SIM</xTexto></ObsCont>", xml);
        Assert.Contains("<ObsCont xCampo=\"TranspProprio\"><xTexto>NAO</xTexto></ObsCont>", xml);
        Assert.Contains(
            "<xObs>Motorista: JOAO DA SILVA | CPF: 52998224725 | Placa: ABC1D23, REB1A11, REB2B22 | FRETE CONFORME CONTRATO</xObs>", xml);
    }

    [Fact]
    public async Task Homologation_replaces_every_party_name()
    {
        var key = await SeedConfirmedAsync();
        await AddAllPartiesAsync(key);
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        var xml = Assert.Single(_sefaz.Sent).Xml;
        const string literal = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
        Assert.Equal(literal, CteIssueInputAssembler.HomologationName);
        foreach (var tag in new[] { "rem", "exped", "receb", "dest", "toma4" })
        {
            var start = xml.IndexOf($"<{tag}>", StringComparison.Ordinal);
            Assert.True(start >= 0, $"<{tag}> ausente");
            var end = xml.IndexOf($"</{tag}>", start, StringComparison.Ordinal);
            Assert.Contains($"<xNome>{literal}</xNome>", xml[start..end]);
        }
        Assert.Contains("<tpAmb>2</tpAmb>", xml);
        Assert.DoesNotContain("ARMAZEM DESTINO LTDA", xml);
        Assert.DoesNotContain("COSTA PRODUTOR RURAL LTDA", xml);
        Assert.DoesNotContain("EXPEDIDOR LTDA", xml);
        Assert.DoesNotContain("RECEBEDOR LTDA", xml);
        Assert.DoesNotContain("TOMADOR LTDA", xml);
    }

    [Fact]
    public async Task Production_keeps_real_party_names()
    {
        var key = await SeedConfirmedAsync(NfeEnvironment.Production);
        await AddAllPartiesAsync(key);
        _sefaz.AuthorizeResponses.Enqueue(Ok);

        await Transmit().TransmitAsync(key, "tester");

        var xml = Assert.Single(_sefaz.Sent).Xml;
        Assert.DoesNotContain("AMBIENTE DE HOMOLOGACAO", xml);
        foreach (var name in new[] { "COSTA PRODUTOR RURAL LTDA", "ARMAZEM DESTINO LTDA", "EXPEDIDOR LTDA", "RECEBEDOR LTDA", "TOMADOR LTDA" })
            Assert.Contains($"<xNome>{name}</xNome>", xml);
    }

    private void AddParty(TransportDocument doc, TransportDocumentParty party)
    {
        party.TransportDocumentKey = doc.Key;
        _db.Context.TransportDocumentParties.Add(party);
    }

    private async Task AddAllPartiesAsync(Guid key)
    {
        var doc = await _db.Context.TransportDocuments.SingleAsync(d => d.Key == key);
        doc.TakerRole = (int)CtePartyRole.OtherTaker;
        AddParty(doc, Party(CtePartyRole.Shipper, "44555666000133", "EXPEDIDOR LTDA", "3521705", "ITABERA"));
        AddParty(doc, Party(CtePartyRole.Receiver, "55666777000144", "RECEBEDOR LTDA", "3550308", "SAO PAULO"));
        AddParty(doc, Party(CtePartyRole.OtherTaker, "66777888000155", "TOMADOR LTDA", "3550308", "SAO PAULO"));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Consult_recovers_processing_with_stored_xml()
    {
        var key = await SeedConfirmedAsync();
        Timeout(_sefaz);
        _sefaz.ConsultResponses.Enqueue(NotFound);
        await Transmit().TransmitAsync(key, "tester");
        Assert.Equal(CteStatus.Processing, (await LoadAsync(key)).Status);

        _sefaz.ConsultResponses.Enqueue(OkForSent(0));
        var result = await Consult().ConsultAsync(key);

        Assert.Equal(100, result.StatusCode);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.Equal(NfeProcComposer.SignedDigest(_sefaz.Sent[0].Xml), NfeProcComposer.ProtocolDigest(doc.Xmls.Single(x => x.Kind == CteXmlKind.Authorized).Xml));
        Assert.Equal(1, _sefaz.AuthorizeCalls);
    }
}
