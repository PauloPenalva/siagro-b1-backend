using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

public class NfeDistributionDocumentParserTests
{
    private const string Key = "35261012345678000195550010000001231481516230";
    private const string Ns = "http://www.portalfiscal.inf.br/nfe";

    [Fact]
    public void Parse_ResNFe_ReadsSummary()
    {
        var xml = $"""
            <resNFe versao="1.01" xmlns="{Ns}"><chNFe>{Key}</chNFe><CNPJ>12345678000195</CNPJ><xNome>FAZENDA BOA VISTA LTDA</xNome>
            <IE>111222333444</IE><dhEmi>2026-10-15T10:30:00-03:00</dhEmi><tpNF>1</tpNF><vNF>15234.56</vNF><digVal>abc=</digVal>
            <dhRecbto>2026-10-15T10:31:00-03:00</dhRecbto><nProt>135260000000001</nProt><cSitNFe>1</cSitNFe></resNFe>
            """;

        var parsed = NfeDistributionDocumentParser.Parse(new NfeDistributionDocument(10, "resNFe_v1.01", xml));

        Assert.Equal(DfeKind.Summary, parsed.Kind);
        Assert.Equal(Key, parsed.AccessKey);
        Assert.Equal("12345678000195", parsed.IssuerTaxId);
        Assert.Equal("FAZENDA BOA VISTA LTDA", parsed.IssuerName);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 10, 30, 0, TimeSpan.FromHours(-3)), parsed.IssueDate);
        Assert.Equal(15234.56m, parsed.TotalValue);
        Assert.Equal(1, parsed.InvoiceSituation);
        Assert.Null(parsed.FullXml);
    }

    [Fact]
    public void Parse_ProcNFe_ReturnsFullXmlAndKey()
    {
        var xml = $"""
            <nfeProc versao="4.00" xmlns="{Ns}"><NFe><infNFe Id="NFe{Key}" versao="4.00"><ide><dhEmi>2026-10-15T10:30:00-03:00</dhEmi></ide>
            <emit><CNPJ>12345678000195</CNPJ><xNome>FAZENDA BOA VISTA LTDA</xNome></emit>
            <total><ICMSTot><vNF>15234.56</vNF></ICMSTot></total></infNFe></NFe>
            <protNFe versao="4.00"><infProt><chNFe>{Key}</chNFe><cStat>100</cStat></infProt></protNFe></nfeProc>
            """;

        var parsed = NfeDistributionDocumentParser.Parse(new NfeDistributionDocument(11, "procNFe_v4.00", xml));

        Assert.Equal(DfeKind.FullInvoice, parsed.Kind);
        Assert.Equal(Key, parsed.AccessKey);
        Assert.Equal("12345678000195", parsed.IssuerTaxId);
        Assert.Equal("FAZENDA BOA VISTA LTDA", parsed.IssuerName);
        Assert.Equal(15234.56m, parsed.TotalValue);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 10, 30, 0, TimeSpan.FromHours(-3)), parsed.IssueDate);
        Assert.Equal(xml, parsed.FullXml);
    }

    [Fact]
    public void Parse_ResEventoCancellation_ReturnsEvent()
    {
        var xml = $"""
            <resEvento versao="1.01" xmlns="{Ns}"><cOrgao>91</cOrgao><CNPJ>12345678000195</CNPJ><chNFe>{Key}</chNFe>
            <dhEvento>2026-10-16T09:00:00-03:00</dhEvento><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento>
            <xEvento>Cancelamento homologado</xEvento><dhRecbto>2026-10-16T09:00:05-03:00</dhRecbto><nProt>135260000000002</nProt></resEvento>
            """;

        var parsed = NfeDistributionDocumentParser.Parse(new NfeDistributionDocument(12, "resEvento_v1.01", xml));

        Assert.Equal(DfeKind.Event, parsed.Kind);
        Assert.Equal(Key, parsed.AccessKey);
        Assert.Equal(110111, parsed.EventType);
    }

    [Fact]
    public void Parse_UnknownSchema_ReturnsOther()
    {
        var parsed = NfeDistributionDocumentParser.Parse(
            new NfeDistributionDocument(13, "algumaCoisa_v1.00", $"<algumaCoisa xmlns=\"{Ns}\"/>"));

        Assert.Equal(DfeKind.Other, parsed.Kind);
        Assert.Null(parsed.AccessKey);
    }
}
