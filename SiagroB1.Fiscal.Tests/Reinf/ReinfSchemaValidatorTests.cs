using System.Xml;
using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfSchemaValidatorTests
{
    private static XmlDocument Load(string xml)
    {
        var d = new XmlDocument { PreserveWhitespace = true };
        d.LoadXml(xml);
        return d;
    }

    [Fact]
    public void Validate_UnsignedEvent_ReportsMissingSignature()
    {
        var doc = Load($"<Reinf xmlns=\"http://www.reinf.esocial.gov.br/schemas/evtReabreEvPer/v2_01_02\"><evtReabreEvPer id=\"ID1\"><ideEvento><perApur>2026-10</perApur><tpAmb>2</tpAmb><procEmi>1</procEmi><verProc>1</verProc></ideEvento><ideContri><tpInsc>1</tpInsc><nrInsc>12345678</nrInsc></ideContri></evtReabreEvPer></Reinf>");
        var errors = new ReinfSchemaValidator().Validate(doc);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("Signature"));
    }

    [Fact]
    public void Validate_UnknownNamespace_ReturnsMessage()
    {
        var doc = Load("<Reinf xmlns=\"urn:x\"><a xmlns=\"urn:x\"/></Reinf>");
        var errors = new ReinfSchemaValidator().Validate(doc);
        Assert.Equal(new[] { "Namespace desconhecido: urn:x" }, errors);
    }
}
