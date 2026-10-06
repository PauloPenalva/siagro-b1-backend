using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>As cartas registradas ficam na tabela filha do documento, com o XML do evento.</summary>
public class NfeCorrectionStoreTests
{
    [Fact]
    public async Task Sales_store_adds_and_reads_corrections()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var store = new SalesInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;

        store.AddCorrection(invoice, 1, "Texto da primeira carta", FakeNfeSefazClient.CorrectionRegistered(1, "Texto da primeira carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        Assert.Equal([1], await store.CorrectionSequencesAsync(scenario.InvoiceKey));
        Assert.Contains("<procEventoNFe", await store.CorrectionXmlAsync(scenario.InvoiceKey, 1));
        Assert.Null(await store.CorrectionXmlAsync(scenario.InvoiceKey, 2));
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(FakeNfeSefazClient.CorrectionProtocol(1), row.Protocol);
        Assert.Equal(FakeNfeSefazClient.CorrectionRegisteredAt.DateTime, row.RegisteredAt);
        Assert.Equal(135, row.StatusCode);
        Assert.Equal("tester", row.CreatedBy);
    }

    [Fact]
    public async Task Purchase_store_adds_and_reads_corrections()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var store = new PurchaseInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;

        store.AddCorrection(invoice, 3, "Texto da terceira carta", FakeNfeSefazClient.CorrectionRegistered(3, "Texto da terceira carta"), "tester");
        await scenario.Db.SaveChangesAsync();

        Assert.Equal([3], await store.CorrectionSequencesAsync(scenario.InvoiceKey));
        Assert.NotNull(await store.CorrectionXmlAsync(scenario.InvoiceKey, 3));
    }
}
