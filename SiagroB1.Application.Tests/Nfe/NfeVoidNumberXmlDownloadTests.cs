using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeVoidNumberXmlDownloadTests
{
    [Fact]
    public async Task Void_number_xml_is_downloaded_with_series_and_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await RejectAndCancelSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "NF-e rejeitada e documento cancelado", "tester");

        var (bytes, fileName) = await new SalesInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal("1-000000233-procInutNFe.xml", fileName);
        Assert.Contains("<procInutNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Void_without_proc_xml_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));

        Assert.Equal("Esta inutilização não tem comprovante.", ex.Message);
    }

    [Fact]
    public async Task Purchase_void_number_xml_is_downloaded()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Entrada propria rejeitada e cancelada", "tester");

        var (_, fileName) = await new PurchaseInvoicesNfeVoidNumberXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal("1-000000233-procInutNFe.xml", fileName);
    }
}
