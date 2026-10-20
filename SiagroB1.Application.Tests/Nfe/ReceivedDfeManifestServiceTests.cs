using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Manifestação do destinatário e download do XML (spec 2026-10-20, T4).</summary>
public class ReceivedDfeManifestServiceTests
{
    internal const string Key = "35261011222333000181550010000000011481516231";

    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 15, 0, 0, TimeSpan.FromHours(-3));

    internal static ReceivedDfeManifestService Service(NfeScenario s, FakeNfeSefazClient sefaz) =>
        new(s.Db, new BranchNfeSettingsService(s.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
            NullLogger<ReceivedDfeManifestService>.Instance, () => Now);

    internal static async Task<ReceivedDfe> SeedAsync(
        NfeScenario s, DfeManifestationStatus status = DfeManifestationStatus.None,
        DfeInvoiceSituation situation = DfeInvoiceSituation.Authorized, string? xml = null)
    {
        var row = new ReceivedDfe
        {
            BranchCode = "01", AccessKey = Key, Situation = situation, ManifestationStatus = status, Xml = xml, CreatedAt = Now,
        };
        s.Db.Context.ReceivedDfes.Add(row);
        await s.Db.SaveChangesAsync();
        return row;
    }

    internal static NfeDistributionResult FullInvoiceResult() => new(138, "Documento localizado", 5, 5,
        [new NfeDistributionDocument(5, "procNFe_v4.00.xsd",
            $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{Key}\"><ide><dhEmi>2026-10-15T10:00:00-03:00</dhEmi></ide>" +
            "<emit><CNPJ>11222333000181</CNPJ><xNome>FORNECEDOR LTDA</xNome></emit><total><ICMSTot><vNF>1500.50</vNF></ICMSTot></total></infNFe></NFe></nfeProc>")]);

    internal static NfeDistributionResult NotAvailableResult() => new(137, "Nenhum documento localizado", 0, 0, []);

    private static void Registered(FakeNfeSefazClient sefaz, int status = 135) =>
        sefaz.ManifestationResponses.Enqueue(r => FakeNfeSefazClient.ManifestationRegistered(r, status));

    [Fact]
    public async Task Acknowledge_FromNone_Registers()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s);
        var sefaz = new FakeNfeSefazClient();
        Registered(sefaz);
        sefaz.EnqueueDistributionByKey(NotAvailableResult());

        var result = await Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Acknowledgement, null);

        Assert.Equal(DfeManifestationStatus.Acknowledged, result.ManifestationStatus);
        Assert.Equal(FakeNfeSefazClient.ManifestationProtocol, result.ManifestationProtocol);
        Assert.NotNull(result.ManifestedAt);
        Assert.Contains("<procEventoNFe", result.ManifestationProcEventXml);
        var request = Assert.Single(sefaz.ManifestationRequests);
        Assert.Equal(Key, request.AccessKey);
        Assert.Equal("12345678000195", request.RecipientTaxId);
        Assert.Equal(Now, request.EventAt);
    }

    [Fact]
    public async Task Confirm_AfterConfirmed_IsRejected()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Confirmed);
        var sefaz = new FakeNfeSefazClient();

        await Assert.ThrowsAsync<DefaultException>(() => Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Confirmation, null));

        Assert.Empty(sefaz.ManifestationRequests);
    }

    [Fact]
    public async Task Unknown_AfterConfirmed_IsRejected()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Confirmed);
        var sefaz = new FakeNfeSefazClient();

        await Assert.ThrowsAsync<DefaultException>(() => Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Unknown, null));

        Assert.Empty(sefaz.ManifestationRequests);
    }

    [Fact]
    public async Task NotPerformed_WithoutJustification_IsRejected()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Acknowledged);
        var sefaz = new FakeNfeSefazClient();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.NotPerformed, "  curta  "));

        Assert.Equal("Informe uma justificativa entre 15 e 255 caracteres.", e.Message);
        Assert.Empty(sefaz.ManifestationRequests);
    }

    [Fact]
    public async Task NotPerformed_TrimsJustification_AndRegisters()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Acknowledged);
        var sefaz = new FakeNfeSefazClient();
        Registered(sefaz);

        var result = await Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.NotPerformed, "  Mercadoria não foi recebida.  ");

        Assert.Equal(DfeManifestationStatus.NotPerformed, result.ManifestationStatus);
        Assert.Equal("Mercadoria não foi recebida.", result.ManifestationJustification);
        Assert.Equal("Mercadoria não foi recebida.", sefaz.ManifestationRequests[0].Justification);
    }

    [Fact]
    public async Task Duplicate573_IsTreatedAsRegistered()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s);
        var sefaz = new FakeNfeSefazClient();
        Registered(sefaz, NfeStatusCodes.DuplicateEvent);
        sefaz.EnqueueDistributionByKey(NotAvailableResult());

        var result = await Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Acknowledgement, null);

        Assert.Equal(DfeManifestationStatus.Acknowledged, result.ManifestationStatus);
    }

    [Fact]
    public async Task Rejection_KeepsStatus_AndThrowsWithReason()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s);
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueManifestation(new NfeEventResult(494, "Rejeição: Chave de Acesso inexistente"));

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Acknowledgement, null));

        Assert.Equal("494 - Rejeição: Chave de Acesso inexistente", e.Message);
        var saved = await s.Db.Context.ReceivedDfes.AsNoTracking().SingleAsync();
        Assert.Equal(DfeManifestationStatus.None, saved.ManifestationStatus);
        Assert.Null(saved.ManifestationProtocol);
    }

    [Fact]
    public async Task Confirm_CancelledInvoice_IsRejected()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Acknowledged, DfeInvoiceSituation.Cancelled);
        var sefaz = new FakeNfeSefazClient();

        await Assert.ThrowsAsync<DefaultException>(() => Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Confirmation, null));

        Assert.Empty(sefaz.ManifestationRequests);
    }

    [Fact]
    public async Task Confirm_DownloadsXml_WhenMissing()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Acknowledged);
        var sefaz = new FakeNfeSefazClient();
        Registered(sefaz);
        sefaz.EnqueueDistributionByKey(FullInvoiceResult());

        var result = await Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Confirmation, null);

        Assert.Equal(DfeManifestationStatus.Confirmed, result.ManifestationStatus);
        Assert.Contains("<nfeProc", result.Xml);
        Assert.Equal(Now, result.XmlDownloadedAt);
        var call = Assert.Single(sefaz.DistributionByKeyCalls);
        Assert.Equal("12345678000195", call.RecipientTaxId);
        Assert.Equal(Key, call.AccessKey);
    }

    [Fact]
    public async Task Confirm_XmlDownloadFails_KeepsManifestation()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await SeedAsync(s, DfeManifestationStatus.Acknowledged);
        var sefaz = new FakeNfeSefazClient();
        Registered(sefaz);
        sefaz.DistributionByKeyResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var result = await Service(s, sefaz).ExecuteAsync(row.Id, NfeManifestationType.Confirmation, null);

        Assert.Equal(DfeManifestationStatus.Confirmed, result.ManifestationStatus);
        Assert.Null(result.Xml);
    }
}
