using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCorrectionXmlDownloadTests
{
    [Fact]
    public async Task Correction_xml_is_downloaded_with_key_and_sequence_in_the_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var store = new SalesInvoiceNfeStore(scenario.Db);
        store.AddCorrection((await store.FindAsync(scenario.InvoiceKey))!, 2, "Texto da segunda carta",
            FakeNfeSefazClient.CorrectionRegistered(2, "Texto da segunda carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        var (bytes, fileName) = await new SalesInvoicesNfeCorrectionXmlDownloadService(scenario.Db)
            .ExecuteAsync(scenario.InvoiceKey, 2);

        Assert.Equal($"{AccessKey}-cce-2-procEventoNFe.xml", fileName);
        Assert.Contains("<procEventoNFe", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Missing_correction_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeCorrectionXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey, 1));

        Assert.Equal("Carta de correção não encontrada ou sem o XML do evento.", ex.Message);
    }
}
