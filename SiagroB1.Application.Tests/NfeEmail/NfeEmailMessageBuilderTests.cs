using System.Text;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.NfeEmail;

public class NfeEmailMessageBuilderTests
{
    private static readonly byte[] Danfe = Encoding.ASCII.GetBytes("%PDF-fake");

    [Fact]
    public void Authorized_subject_and_attachments()
    {
        var (subject, body, attachments) = NfeEmailMessageBuilder.Build(
            NfeEmailEventType.Authorized, NfeEmailTestXml.ProcNfe(), null, Danfe);

        Assert.Equal("NF-e nº 293 série 1 – CEAGUI COMERCIO DE CEREAIS LTDA – Autorizada", subject);
        Assert.Equal([$"{NfeEmailTestXml.AccessKey}-procNFe.xml", "DANFE-293.pdf"], attachments.Select(a => a.FileName));
        Assert.Equal("application/pdf", attachments[1].ContentType);
        Assert.Same(Danfe, attachments[1].Content);
        Assert.Contains(NfeEmailTestXml.AccessKey, body);
        Assert.Contains("141260000012345", body);
        Assert.Contains("FAZENDA BOA VISTA", body);
        Assert.Contains("150.000,00", body);
    }

    [Fact]
    public void Cancelled_with_event_xml_attaches_proc_event()
    {
        var (subject, _, attachments) = NfeEmailMessageBuilder.Build(
            NfeEmailEventType.Cancelled, NfeEmailTestXml.ProcNfe(), NfeEmailTestXml.ProcEvent, Danfe);

        Assert.EndsWith("– Cancelada", subject);
        Assert.Equal(
            [$"{NfeEmailTestXml.AccessKey}-procEventoNFe.xml", "DANFE-293.pdf"],
            attachments.Select(a => a.FileName));
    }

    [Fact]
    public void Cancelled_without_event_xml_has_only_danfe_and_notice()
    {
        var (_, body, attachments) = NfeEmailMessageBuilder.Build(
            NfeEmailEventType.Cancelled, NfeEmailTestXml.ProcNfe(), null, Danfe);

        Assert.Equal(["DANFE-293.pdf"], attachments.Select(a => a.FileName));
        Assert.Contains("cancelada fora do Siagro", body);
    }

    [Fact]
    public void Body_html_encodes_partner_names()
    {
        var (subject, body, _) = NfeEmailMessageBuilder.Build(
            NfeEmailEventType.Authorized, NfeEmailTestXml.ProcNfe("A &amp; B &lt;LTDA&gt;"), null, Danfe);

        Assert.Contains("A & B <LTDA>", subject);
        Assert.Contains("A &amp; B &lt;LTDA&gt;", body);
    }
}
