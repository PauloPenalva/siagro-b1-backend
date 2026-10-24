using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfResponseParserTests
{
    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reinf", "Samples", name));

    [Fact]
    public void Processing_batch_has_protocol_and_no_events()
    {
        var r = ReinfResponseParser.Parse(Sample("lote-processando.xml"));
        Assert.Equal(1, r.ResponseCode);
        Assert.Equal("2.202610.1234567", r.Protocol);
        Assert.Empty(r.Events);
        Assert.Empty(r.Occurrences);
    }

    [Fact]
    public void Processed_batch_has_accepted_and_rejected_events()
    {
        var r = ReinfResponseParser.Parse(Sample("lote-processado.xml"));
        Assert.Equal(2, r.ResponseCode);
        Assert.Equal(2, r.Events.Count);
        var ok = r.Events[0];
        Assert.True(ok.Accepted);
        Assert.Equal("ID1123456780000002026102410000000001", ok.EventId);
        Assert.Equal("12345-01-1234-1234-12345", ok.Receipt);
        var bad = r.Events[1];
        Assert.False(bad.Accepted);
        Assert.Null(bad.Receipt);
        Assert.Equal(["MS1234 - Produtor rural nao encontrado."], bad.Occurrences);
        Assert.Contains("evtTotal", bad.RawXml);
    }

    [Fact]
    public void Refused_batch_has_batch_occurrences()
    {
        var r = ReinfResponseParser.Parse(Sample("lote-recusado.xml"));
        Assert.True(r.ResponseCode >= 3);
        Assert.Equal(["MS0001 - Certificado invalido."], r.Occurrences);
        Assert.Empty(r.Events);
    }
}
