namespace SiagroB1.Application.Tests.NfeEmail;

/// <summary>procNFe mínimo (só os campos que o e-mail lê) para os testes do envio.</summary>
internal static class NfeEmailTestXml
{
    public const string AccessKey = "41261012345678000190550010000002931000002930";

    public static string ProcNfe(string emitter = "CEAGUI COMERCIO DE CEREAIS LTDA") => $"""
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
          <NFe><infNFe Id="NFe{AccessKey}" versao="4.00">
            <ide><serie>1</serie><nNF>293</nNF><dhEmi>2026-10-18T10:15:00-03:00</dhEmi></ide>
            <emit><CNPJ>12345678000190</CNPJ><xNome>{emitter}</xNome></emit>
            <dest><CNPJ>99888777000166</CNPJ><xNome>FAZENDA BOA VISTA</xNome></dest>
            <total><ICMSTot><vNF>150000.00</vNF></ICMSTot></total>
          </infNFe></NFe>
          <protNFe versao="4.00"><infProt><chNFe>{AccessKey}</chNFe><nProt>141260000012345</nProt></infProt></protNFe>
        </nfeProc>
        """;

    public const string ProcEvent = """<procEventoNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00"><evento/></procEventoNFe>""";
}
