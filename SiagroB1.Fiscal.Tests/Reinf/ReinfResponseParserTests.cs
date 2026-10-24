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

    [Fact]
    public void Warning_event_is_accepted_and_keeps_occurrences()
    {
        var r = ReinfResponseParser.Parse(Sample("lote-aviso.xml"));
        var ev = Assert.Single(r.Events);
        Assert.True(ev.Accepted);
        Assert.Equal("12345-01-1234-1234-12345", ev.Receipt);
        Assert.Equal(["MS9999 - Advertencia de teste."], ev.Occurrences);
    }

    [Theory]
    [InlineData("<Reinf xmlns=\"http://www.reinf.esocial.gov.br/schemas/retornoLoteEventosAssincrono/v1_00_00\"><retornoLoteEventosAssincrono><status><descResposta>x</descResposta></status></retornoLoteEventosAssincrono></Reinf>")]
    [InlineData("<Reinf xmlns=\"http://www.reinf.esocial.gov.br/schemas/retornoLoteEventosAssincrono/v1_00_00\"><retornoLoteEventosAssincrono><status><cdResposta>abc</cdResposta></status></retornoLoteEventosAssincrono></Reinf>")]
    public void Missing_or_invalid_response_code_throws(string xml) =>
        Assert.Throws<FormatException>(() => ReinfResponseParser.Parse(xml));
}
