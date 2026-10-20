using System.Text;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

public class ReceivedDfeXmlServiceTests
{
    private static ReceivedDfeXmlService Service(NfeScenario s, FakeNfeSefazClient sefaz) =>
        new(ReceivedDfeManifestServiceTests.Service(s, sefaz));

    [Fact]
    public async Task GetXml_AlreadyStored_DoesNotCallSefaz()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await ReceivedDfeManifestServiceTests.SeedAsync(s, xml: "<nfeProc/>");
        var sefaz = new FakeNfeSefazClient();

        var (content, name) = await Service(s, sefaz).GetAsync(row.Id);

        Assert.Equal("<nfeProc/>", Encoding.UTF8.GetString(content));
        Assert.Equal($"{ReceivedDfeManifestServiceTests.Key}-procNFe.xml", name);
        Assert.Empty(sefaz.DistributionByKeyCalls);
    }

    [Fact]
    public async Task GetXml_FromNone_SendsAcknowledgementThenDownloads()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await ReceivedDfeManifestServiceTests.SeedAsync(s);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ManifestationResponses.Enqueue(r => FakeNfeSefazClient.ManifestationRegistered(r));
        sefaz.EnqueueDistributionByKey(ReceivedDfeManifestServiceTests.FullInvoiceResult());

        var (content, name) = await Service(s, sefaz).GetAsync(row.Id);

        Assert.Contains("<nfeProc", Encoding.UTF8.GetString(content));
        Assert.EndsWith("-procNFe.xml", name);
        Assert.Equal(NfeManifestationType.Acknowledgement, Assert.Single(sefaz.ManifestationRequests).Type);
        Assert.Single(sefaz.DistributionByKeyCalls);
    }

    [Fact]
    public async Task GetXml_NotAvailable_Throws()
    {
        var s = await NfeTestSeed.SeedAsync();
        var row = await ReceivedDfeManifestServiceTests.SeedAsync(s, DfeManifestationStatus.Acknowledged);
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistributionByKey(ReceivedDfeManifestServiceTests.NotAvailableResult());

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(s, sefaz).GetAsync(row.Id));

        Assert.Equal("O XML ainda não está disponível na SEFAZ. Tente novamente mais tarde.", e.Message);
    }
}
