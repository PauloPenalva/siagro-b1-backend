using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCancellationXmlDownloadTests
{
    [Fact]
    public async Task Cancellation_xml_is_downloaded_with_the_event_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));
        await NfeCancelTestServices.SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Venda desfeita pelo cliente", "tester");

        var (bytes, fileName) = await new SalesInvoicesNfeCancellationXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal($"{NfeCancelTestServices.AccessKey}-procEventoNFe.xml", fileName);
        Assert.Contains("<procEventoNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Missing_cancellation_xml_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeCancellationXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));

        Assert.Equal("Este documento não tem o XML do cancelamento da NF-e.", ex.Message);
    }
}
