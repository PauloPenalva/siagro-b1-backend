using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

public class SalesInvoicesNfeXmlDownloadServiceTests
{
    [Fact]
    public async Task Returns_the_authorized_proc_named_by_the_access_key()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = scenario.Db.Context.SalesInvoices.Single();
        invoice.ChaveNFe = "35261012345678000195550010000000011481516230";
        scenario.Db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = NfeXmlKind.Authorized,
            Xml = "<nfeProc/>", CreatedAt = DateTime.Now,
        });
        await scenario.Db.SaveChangesAsync();

        var (bytes, fileName) = await new SalesInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(invoice.Key);

        Assert.Equal("35261012345678000195550010000000011481516230-procNFe.xml", fileName);
        Assert.Equal("<nfeProc/>", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));
    }

    [Fact]
    public async Task With_only_a_signed_xml_it_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        scenario.Db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = scenario.InvoiceKey, Kind = NfeXmlKind.Signed,
            Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        await scenario.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));
    }
}
