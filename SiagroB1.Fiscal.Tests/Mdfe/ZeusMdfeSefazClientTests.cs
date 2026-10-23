using MDFe.Classes.Extensoes;
using MDFe.Classes.Informacoes.Evento.CorpoEvento;
using MDFe.Classes.Informacoes.Evento.Flags;
using SiagroB1.Fiscal.Mdfe;

namespace SiagroB1.Fiscal.Tests.Mdfe;

/// <summary>Forma dos pedidos montados pelo cliente Zeus (sem rede; o envio real fica para o E2E).</summary>
public class ZeusMdfeSefazClientTests
{
    private const string Key = "43261012345678000195580010000000011123456780";

    [Fact]
    public void Close_event_serializes_id_organ_and_closing_place()
    {
        MdfeZeusConfiguration.PinGlobalLayout();
        var configuration = MdfeZeusConfiguration.Create(new MdfeServiceSettings(
            SiagroB1.Domain.Enums.NfeEnvironment.Homologation, "RS", null!, MdfeZeusConfiguration.DefaultSchemasDirectory));
        var container = new MDFeEvEncMDFe
        {
            DescEvento = "Encerramento", NProt = "943260000000001", DtEnc = new DateTime(2026, 10, 23),
            CUF = DFe.Classes.Entidades.Estado.PR, CMun = 4106902,
        };

        var evento = ZeusMdfeSefazClient.BuildEvent(MDFeTipoEvento.Encerramento, 1, Key, "12345678000195",
            new DateTimeOffset(2026, 10, 23, 11, 0, 0, TimeSpan.FromHours(-3)), container, configuration);
        var xml = evento.XmlString();

        Assert.Contains($"Id=\"ID110112{Key}01\"", xml);
        Assert.Contains("<cOrgao>43</cOrgao>", xml);
        Assert.Contains("<tpAmb>2</tpAmb>", xml);
        Assert.Contains("<tpEvento>110112</tpEvento>", xml);
        Assert.Contains("<nSeqEvento>1</nSeqEvento>", xml);
        Assert.Contains("<dtEnc>2026-10-23</dtEnc>", xml);
        Assert.Contains("<cUF>41</cUF>", xml);
        Assert.Contains("<cMun>4106902</cMun>", xml);
        Assert.Matches(@"<dhEvento>2026-10-23T\d\d:00:00[-+]\d\d:\d\d</dhEvento>", xml);
    }

    [Fact]
    public void Wsdl_configuration_carries_the_opened_certificate_and_layout()
    {
        var configuration = MdfeZeusConfiguration.Create(new MdfeServiceSettings(
            SiagroB1.Domain.Enums.NfeEnvironment.Homologation, "RS", null!, MdfeZeusConfiguration.DefaultSchemasDirectory,
            TimeoutMilliseconds: 12_345));

        var wsdl = ZeusMdfeSefazClient.Wsdl("https://x", configuration, null!);

        Assert.Equal("3.00", wsdl.Versao);
        Assert.Equal("43", wsdl.CodigoIbgeEstado);
        Assert.Equal(12_345, wsdl.TimeOut);
        Assert.Equal("https://x", wsdl.Url);
    }
}
