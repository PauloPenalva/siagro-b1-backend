using System.Xml;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Mdfe;

/// <summary>Retornos de exemplo no formato dos XSDs 3.00 (Schemas/MDFe); nenhuma chamada à SEFAZ.</summary>
public class MdfeSefazResponseMapperTests
{
    private const string Key = "43261012345678000195580010000000011123456780";
    private const string Ns = "http://www.portalfiscal.inf.br/mdfe";

    private static string ProtMdfe(int status, string reason) =>
        $"<protMDFe versao=\"3.00\"><infProt Id=\"MDFe943260000000001\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
        $"<chMDFe>{Key}</chMDFe><dhRecbto>2026-10-23T10:00:05-03:00</dhRecbto><nProt>943260000000001</nProt>" +
        $"<digVal>abc123=</digVal><cStat>{status}</cStat><xMotivo>{reason}</xMotivo></infProt></protMDFe>";

    private static string EventoMdfe(int type, int sequence, string detail) =>
        $"<eventoMDFe versao=\"3.00\" xmlns=\"{Ns}\"><infEvento Id=\"ID{type}{Key}{sequence:D2}\"><cOrgao>43</cOrgao><tpAmb>2</tpAmb>" +
        $"<CNPJ>12345678000195</CNPJ><chMDFe>{Key}</chMDFe><dhEvento>2026-10-23T11:00:00-03:00</dhEvento><tpEvento>{type}</tpEvento>" +
        $"<nSeqEvento>{sequence}</nSeqEvento><detEvento versaoEvento=\"3.00\">{detail}</detEvento></infEvento>" +
        "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><SignatureValue>sig=</SignatureValue></Signature></eventoMDFe>";

    private static string RetEvento(int type, int status, string reason, string? protocol) =>
        $"<retEventoMDFe versao=\"3.00\" xmlns=\"{Ns}\"><infEvento><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic><cOrgao>43</cOrgao>" +
        $"<cStat>{status}</cStat><xMotivo>{reason}</xMotivo><chMDFe>{Key}</chMDFe><tpEvento>{type}</tpEvento><xEvento>Evento</xEvento>" +
        $"<nSeqEvento>1</nSeqEvento><dhRegEvento>2026-10-23T11:00:03-03:00</dhRegEvento>" +
        (protocol is null ? "" : $"<nProt>{protocol}</nProt>") + "</infEvento></retEventoMDFe>";

    [Fact]
    public void Authorized_100_maps_protocol_and_mdfeProc()
    {
        var xml = $"<retMDFe versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                  $"<cStat>100</cStat><xMotivo>Autorizado o uso do MDF-e</xMotivo>{ProtMdfe(100, "Autorizado o uso do MDF-e")}</retMDFe>";

        var result = MdfeSefazResponseMapper.FromAuthorization(xml);

        Assert.Equal(100, result.StatusCode);
        Assert.True(NfeStatusCodes.IsAuthorized(result.StatusCode));
        Assert.Equal("Autorizado o uso do MDF-e", result.Reason);
        Assert.Equal("943260000000001", result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 10, 0, 5, TimeSpan.FromHours(-3)), result.ReceivedAt);
        Assert.StartsWith("<protMDFe", result.ProtocolXml);
        Assert.Contains("<digVal>abc123=</digVal>", result.ProtocolXml);

        var signed = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><MDFe xmlns=\"{Ns}\"><infMDFe Id=\"MDFe{Key}\" versao=\"3.00\"/></MDFe>";
        var proc = MdfeProc.Compose(signed, result.ProtocolXml!);

        var document = new XmlDocument();
        document.LoadXml(proc);
        Assert.Equal("mdfeProc", document.DocumentElement!.LocalName);
        Assert.Equal(Ns, document.DocumentElement.NamespaceURI);
        Assert.Equal("3.00", document.DocumentElement.GetAttribute("versao"));
        Assert.Contains($"<MDFe xmlns=\"{Ns}\"><infMDFe Id=\"MDFe{Key}\" versao=\"3.00\"/></MDFe><protMDFe", proc);
        Assert.DoesNotContain("<?xml version=\"1.0\" encoding=\"utf-8\"?>", proc);
        Assert.EndsWith("</protMDFe></mdfeProc>", proc);
    }

    [Fact]
    public void Rejection_maps_code_and_reason()
    {
        var noProtocol = $"<retMDFe versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                         "<cStat>215</cStat><xMotivo>Rejeição: Falha no schema XML</xMotivo></retMDFe>";
        var top = MdfeSefazResponseMapper.FromAuthorization(noProtocol);
        Assert.Equal(215, top.StatusCode);
        Assert.Equal("Rejeição: Falha no schema XML", top.Reason);
        Assert.Null(top.Protocol);
        Assert.Null(top.ProtocolXml);

        var withProtocol = $"<retMDFe versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                           $"<cStat>104</cStat><xMotivo>Processado</xMotivo>{ProtMdfe(611, "Rejeição: Existe MDF-e não encerrado")}</retMDFe>";
        var rejected = MdfeSefazResponseMapper.FromAuthorization(withProtocol);
        Assert.Equal(611, rejected.StatusCode);
        Assert.Equal("Rejeição: Existe MDF-e não encerrado", rejected.Reason);
    }

    [Fact]
    public void Close_135_maps_event_protocol()
    {
        var sent = EventoMdfe(110112, 1,
            "<evEncMDFe><descEvento>Encerramento</descEvento><nProt>943260000000001</nProt><dtEnc>2026-10-23</dtEnc>" +
            "<cUF>43</cUF><cMun>4314902</cMun></evEncMDFe>");
        var ret = RetEvento(110112, 135, "Evento registrado e vinculado a MDF-e", "943260000000077");

        var result = MdfeSefazResponseMapper.FromEvent(sent, ret);

        Assert.Equal(135, result.StatusCode);
        Assert.True(NfeStatusCodes.IsEventRegistered(result.StatusCode));
        Assert.Equal("943260000000077", result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 11, 0, 3, TimeSpan.FromHours(-3)), result.RegisteredAt);
        Assert.Equal(1, result.Sequence);
        Assert.StartsWith($"<procEventoMDFe versao=\"3.00\" xmlns=\"{Ns}\"><eventoMDFe", result.ProcEventXml);
        Assert.Contains("<SignatureValue>sig=</SignatureValue>", result.ProcEventXml);
        Assert.Contains("<nProt>943260000000077</nProt>", result.ProcEventXml);
    }

    [Fact]
    public void Cancel_135_maps_event_protocol()
    {
        var sent = EventoMdfe(110111, 1,
            "<evCancMDFe><descEvento>Cancelamento</descEvento><nProt>943260000000001</nProt>" +
            "<xJust>Viagem cancelada pelo cliente</xJust></evCancMDFe>");
        var ret = RetEvento(110111, 135, "Evento registrado e vinculado a MDF-e", "943260000000088");

        var result = MdfeSefazResponseMapper.FromEvent(sent, ret);

        Assert.Equal(135, result.StatusCode);
        Assert.Equal("943260000000088", result.Protocol);
        Assert.Equal("Viagem cancelada pelo cliente", result.Justification);
        Assert.NotNull(result.ProcEventXml);

        // Rejeitado: sem comprovante.
        var rejected = MdfeSefazResponseMapper.FromEvent(sent, RetEvento(110111, 218, "Rejeição: MDF-e já cancelado", null));
        Assert.Equal(218, rejected.StatusCode);
        Assert.Null(rejected.Protocol);
        Assert.Null(rejected.ProcEventXml);
    }

    [Fact]
    public void NotClosed_list_maps_keys_and_protocols()
    {
        const string other = "43261012345678000195580010000000021123456781";
        var xml = $"<retConsMDFeNaoEnc versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
                  "<cStat>111</cStat><xMotivo>Consulta não encerrados localizou MDF-e nessa situação</xMotivo><cUF>43</cUF>" +
                  $"<infMDFe><chMDFe>{Key}</chMDFe><nProt>943260000000001</nProt></infMDFe>" +
                  $"<infMDFe><chMDFe>{other}</chMDFe><nProt>943260000000002</nProt></infMDFe></retConsMDFeNaoEnc>";

        var result = MdfeSefazResponseMapper.FromNotClosed(xml);

        Assert.Equal(
            [new MdfeNotClosed(Key, "943260000000001"), new MdfeNotClosed(other, "943260000000002")],
            result);
    }

    [Fact]
    public void NotClosed_112_none_found_maps_empty()
    {
        var xml = $"<retConsMDFeNaoEnc versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
                  "<cStat>112</cStat><xMotivo>Consulta não encerrados não localizou MDF-e nessa situação</xMotivo><cUF>43</cUF></retConsMDFeNaoEnc>";

        Assert.Empty(MdfeSefazResponseMapper.FromNotClosed(xml));
    }

    [Fact]
    public void NotClosed_rejection_throws_with_reason()
    {
        var xml = $"<retConsMDFeNaoEnc versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
                  "<cStat>215</cStat><xMotivo>Rejeição: Falha no schema XML</xMotivo><cUF>43</cUF></retConsMDFeNaoEnc>";

        var error = Assert.Throws<MdfeSefazRejectedException>(() => MdfeSefazResponseMapper.FromNotClosed(xml));
        Assert.Equal(215, error.StatusCode);
        Assert.Contains("Falha no schema", error.Message);
    }

    [Fact]
    public void Status_and_consult_map_top_and_protocol()
    {
        var status = MdfeSefazResponseMapper.FromStatus(
            $"<retConsStatServMDFe versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>X</verAplic><cStat>107</cStat>" +
            "<xMotivo>Serviço em Operação</xMotivo><cUF>43</cUF></retConsStatServMDFe>");
        Assert.Equal(107, status.StatusCode);

        var consult = MdfeSefazResponseMapper.FromConsult(
            $"<retConsSitMDFe versao=\"3.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>X</verAplic><cStat>100</cStat>" +
            $"<xMotivo>Autorizado o uso do MDF-e</xMotivo><cUF>43</cUF>{ProtMdfe(100, "Autorizado o uso do MDF-e")}</retConsSitMDFe>");
        Assert.Equal(100, consult.StatusCode);
        Assert.Equal("943260000000001", consult.Protocol);
        Assert.NotNull(consult.ProtocolXml);
    }
}
