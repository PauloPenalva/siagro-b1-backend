using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Cte;

/// <summary>Retornos de exemplo no formato dos XSDs 4.00 (Schemas/CTe); nenhuma chamada à SEFAZ.</summary>
public class CteSefazResponseMapperTests
{
    private const string Key = "43261012345678000195570010000000011123456780";
    private const string Ns = "http://www.portalfiscal.inf.br/cte";

    private static string ProtCte(int status, string reason) =>
        $"<protCTe versao=\"4.00\"><infProt Id=\"CTe143260000000001\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
        $"<chCTe>{Key}</chCTe><dhRecbto>2026-10-22T10:00:05-03:00</dhRecbto><nProt>143260000000001</nProt>" +
        $"<digVal>abc123=</digVal><cStat>{status}</cStat><xMotivo>{reason}</xMotivo></infProt></protCTe>";

    [Fact]
    public void Authorized_100_maps_protocol_and_xml()
    {
        var xml = $"<retCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                  $"<cStat>100</cStat><xMotivo>Autorizado o uso do CT-e</xMotivo>{ProtCte(100, "Autorizado o uso do CT-e")}</retCTe>";

        var result = CteSefazResponseMapper.FromAuthorization(xml);

        Assert.Equal(100, result.StatusCode);
        Assert.True(NfeStatusCodes.IsAuthorized(result.StatusCode));
        Assert.Equal("Autorizado o uso do CT-e", result.Reason);
        Assert.Equal("143260000000001", result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 10, 0, 5, TimeSpan.FromHours(-3)), result.ReceivedAt);
        Assert.NotNull(result.ProtocolXml);
        Assert.StartsWith("<protCTe", result.ProtocolXml);
        Assert.Contains($"<chCTe>{Key}</chCTe>", result.ProtocolXml);
        Assert.Contains("<digVal>abc123=</digVal>", result.ProtocolXml);
    }

    [Fact]
    public void Rejection_maps_code_and_reason()
    {
        // Rejeição na validação do lote: sem protCTe.
        var noProtocol = $"<retCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                         "<cStat>215</cStat><xMotivo>Rejeição: Falha no schema XML</xMotivo></retCTe>";
        // Rejeição da nota: o protCTe traz o código.
        var withProtocol = $"<retCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><cUF>43</cUF><verAplic>RS20261001</verAplic>" +
                           $"<cStat>539</cStat><xMotivo>Rejeição: Duplicidade de CT-e com diferença na Chave de Acesso</xMotivo>" +
                           "</retCTe>";

        var first = CteSefazResponseMapper.FromAuthorization(noProtocol);
        var second = CteSefazResponseMapper.FromAuthorization(withProtocol);

        Assert.Equal(215, first.StatusCode);
        Assert.Equal("Rejeição: Falha no schema XML", first.Reason);
        Assert.Null(first.Protocol);
        Assert.Null(first.ProtocolXml);
        Assert.Equal(539, second.StatusCode);
        Assert.True(NfeStatusCodes.IsDuplicate(second.StatusCode));
    }

    private const string EventProtocol = "143260000000099";

    private static string SentEvent(string type, int sequence, string detail) =>
        $"<eventoCTe versao=\"4.00\" xmlns=\"{Ns}\"><infEvento Id=\"ID{type}{Key}{sequence:D3}\"><cOrgao>43</cOrgao><tpAmb>2</tpAmb>" +
        $"<CNPJ>12345678000195</CNPJ><chCTe>{Key}</chCTe><dhEvento>2026-10-22T11:00:00-03:00</dhEvento>" +
        $"<tpEvento>{type}</tpEvento><nSeqEvento>{sequence}</nSeqEvento><detEvento versaoEvento=\"4.00\">{detail}</detEvento></infEvento>" +
        "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><SignedInfo /></Signature></eventoCTe>";

    private static string RetEvent(string type, int sequence, int status, string reason) =>
        $"<retEventoCTe versao=\"4.00\" xmlns=\"{Ns}\"><infEvento Id=\"ID{EventProtocol}\"><tpAmb>2</tpAmb><verAplic>RS20261001</verAplic>" +
        $"<cOrgao>43</cOrgao><cStat>{status}</cStat><xMotivo>{reason}</xMotivo><chCTe>{Key}</chCTe>" +
        $"<tpEvento>{type}</tpEvento><xEvento>Evento</xEvento><nSeqEvento>{sequence}</nSeqEvento>" +
        $"<dhRegEvento>2026-10-22T11:00:03-03:00</dhRegEvento><nProt>{EventProtocol}</nProt></infEvento></retEventoCTe>";

    [Fact]
    public void Cancel_135_maps_event_protocol()
    {
        var sent = SentEvent("110111", 1,
            "<evCancCTe><descEvento>Cancelamento</descEvento><nProt>143260000000001</nProt><xJust>Frete lancado em duplicidade</xJust></evCancCTe>");

        var result = CteSefazResponseMapper.FromEvent(sent, RetEvent("110111", 1, 135, "Evento registrado e vinculado a CT-e"));

        Assert.Equal(135, result.StatusCode);
        Assert.True(NfeStatusCodes.IsCancellationRegistered(result.StatusCode));
        Assert.Equal(EventProtocol, result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 11, 0, 3, TimeSpan.FromHours(-3)), result.RegisteredAt);
        Assert.Equal("Frete lancado em duplicidade", result.Justification);
        Assert.NotNull(result.ProcEventXml);
        Assert.StartsWith("<procEventoCTe", result.ProcEventXml);
        Assert.Contains("<eventoCTe", result.ProcEventXml);
        Assert.Contains("<retEventoCTe", result.ProcEventXml);
        Assert.Contains("<Signature", result.ProcEventXml);
    }

    [Fact]
    public void Correction_135_maps_sequence()
    {
        var sent = SentEvent("110110", 2,
            "<evCCeCTe><descEvento>Carta de Correcao</descEvento><infCorrecao><grupoAlterado>compl</grupoAlterado>" +
            "<campoAlterado>xObs</campoAlterado><valorAlterado>Peso conferido na balanca</valorAlterado></infCorrecao>" +
            "<xCondUso>A Carta de Correcao e disciplinada pelo Art. 58-B do CONVENIO/SINIEF 06/89</xCondUso></evCCeCTe>");

        var result = CteSefazResponseMapper.FromEvent(sent, RetEvent("110110", 2, 135, "Evento registrado e vinculado a CT-e"));

        Assert.Equal(135, result.StatusCode);
        Assert.Equal(2, result.Sequence);
        Assert.Equal(EventProtocol, result.Protocol);
        Assert.NotNull(result.ProcEventXml);
    }

    [Fact]
    public void Rejected_event_has_no_proc_xml()
    {
        var sent = SentEvent("110111", 1, "<evCancCTe><descEvento>Cancelamento</descEvento><nProt>1</nProt><xJust>Justificativa longa</xJust></evCancCTe>");

        var result = CteSefazResponseMapper.FromEvent(sent, RetEvent("110111", 1, 220, "Rejeição: CT-e autorizado há mais de 7 dias"));

        Assert.Equal(220, result.StatusCode);
        Assert.Null(result.ProcEventXml);
    }

    [Fact]
    public void Consult_authorized_maps_protocol_and_cancelled_maps_event()
    {
        var authorized = $"<retConsSitCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS</verAplic><cStat>100</cStat>" +
                         $"<xMotivo>Autorizado o uso do CT-e</xMotivo><cUF>43</cUF>{ProtCte(100, "Autorizado o uso do CT-e")}</retConsSitCTe>";
        var cancelled = $"<retConsSitCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS</verAplic><cStat>101</cStat>" +
                        $"<xMotivo>Cancelamento de CT-e homologado</xMotivo><cUF>43</cUF>{ProtCte(100, "Autorizado o uso do CT-e")}" +
                        $"<procEventoCTe versao=\"4.00\">{SentEvent("110111", 1, "<evCancCTe><descEvento>Cancelamento</descEvento><nProt>1</nProt><xJust>Cancelado no portal</xJust></evCancCTe>")}" +
                        $"{RetEvent("110111", 1, 135, "Evento registrado")}</procEventoCTe></retConsSitCTe>";

        var first = CteSefazResponseMapper.FromConsult(authorized);
        var second = CteSefazResponseMapper.FromConsult(cancelled);

        Assert.Equal(100, first.StatusCode);
        Assert.Equal("143260000000001", first.Protocol);
        Assert.NotNull(first.ProtocolXml);
        Assert.Equal(101, second.StatusCode);
        Assert.Null(second.Protocol);
        Assert.NotNull(second.CancellationEvent);
        Assert.Equal(EventProtocol, second.CancellationEvent!.Protocol);
        Assert.Equal("Cancelado no portal", second.CancellationEvent.Justification);
    }

    [Fact]
    public void Status_and_void_number_map_codes()
    {
        var status = CteSefazResponseMapper.FromStatus(
            $"<retConsStatServCTe versao=\"4.00\" xmlns=\"{Ns}\"><tpAmb>2</tpAmb><verAplic>RS</verAplic><cStat>107</cStat>" +
            "<xMotivo>Servico em Operacao</xMotivo><cUF>43</cUF><dhRecbto>2026-10-22T10:00:00-03:00</dhRecbto><tMed>1</tMed></retConsStatServCTe>");
        var request = $"<inutCTe versao=\"4.00\" xmlns=\"{Ns}\"><infInut Id=\"ID43123456780001955700100000000500000000005\"><tpAmb>2</tpAmb>" +
                      "<xServ>INUTILIZAR</xServ></infInut></inutCTe>";
        var voided = CteSefazResponseMapper.FromVoidNumber(request,
            $"<retInutCTe versao=\"4.00\" xmlns=\"{Ns}\"><infInut><tpAmb>2</tpAmb><verAplic>RS</verAplic><cStat>102</cStat>" +
            "<xMotivo>Inutilizacao de numero homologado</xMotivo><cUF>43</cUF><nProt>143260000000777</nProt></infInut></retInutCTe>");

        Assert.Equal(107, status.StatusCode);
        Assert.Equal(102, voided.StatusCode);
        Assert.Equal("143260000000777", voided.Protocol);
        Assert.StartsWith("<procInutCTe", voided.ProcXml);
        Assert.Contains("<inutCTe", voided.ProcXml);
        Assert.Contains("<retInutCTe", voided.ProcXml);
    }
}
