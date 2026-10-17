using System.Text;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Leitura do XML do CT-e 4.00 (frete tomado): cabeçalho, tomador, ICMS e chaves transportadas.</summary>
public class SupplierCteXmlReaderTests
{
    private const string Key = "35260800000000000000570010000001231000000017";

    private static byte[] Cte(string tomaXml = "<toma3><toma>3</toma></toma3>",
        string icmsXml = "<ICMS00><CST>00</CST><vBC>1500.00</vBC><pICMS>7.00</pICMS><vICMS>105.00</vICMS></ICMS00>") =>
        Encoding.UTF8.GetBytes($"""
<?xml version="1.0" encoding="UTF-8"?>
<cteProc xmlns="http://www.portalfiscal.inf.br/cte" versao="4.00">
  <CTe>
    <infCte Id="CTe{Key}" versao="4.00">
      <ide><nCT>123</nCT><serie>1</serie><dhEmi>2026-08-06T09:30:00-03:00</dhEmi><tpCTe>0</tpCTe>
        <xMunIni>SORRISO</xMunIni><UFIni>MT</UFIni><xMunFim>SANTOS</xMunFim><UFFim>SP</UFFim>
        {tomaXml}</ide>
      <compl><xObs>Frete de soja</xObs></compl>
      <emit><CNPJ>11111111000191</CNPJ><xNome>TRANSPORTADORA TESTE</xNome></emit>
      <rem><CNPJ>22222222000191</CNPJ></rem>
      <exped><CNPJ>33333333000191</CNPJ></exped>
      <receb><CPF>44444444444</CPF></receb>
      <dest><CNPJ>55555555000191</CNPJ></dest>
      <vPrest><vTPrest>1500.00</vTPrest><vRec>1500.00</vRec></vPrest>
      <imp><ICMS>{icmsXml}</ICMS></imp>
      <infCTeNorm><infDoc>
        <infNFe><chave>35260800000000000000550010000000011000000017</chave></infNFe>
        <infNFe><chave>35260800000000000000550010000000021000000024</chave></infNFe>
      </infDoc></infCTeNorm>
    </infCte>
  </CTe>
  <protCTe versao="4.00"><infProt><chCTe>{Key}</chCTe></infProt></protCTe>
</cteProc>
""");

    [Fact]
    public void Reads_header_values_and_transported_keys()
    {
        var cte = SupplierCteXmlReader.Read(Cte());

        Assert.Equal(Key, cte.AccessKey);
        Assert.Equal("123", cte.Number);
        Assert.Equal("1", cte.Series);
        Assert.Equal(new DateTime(2026, 8, 6, 9, 30, 0), cte.IssueDate);
        Assert.Equal("0", cte.CteType);
        Assert.Equal("MT", cte.StartState);
        Assert.Equal("SP", cte.EndState);
        Assert.Equal("SORRISO", cte.StartCity);
        Assert.Equal("SANTOS", cte.EndCity);
        Assert.Equal("11111111000191", cte.IssuerTaxId);
        Assert.Equal("TRANSPORTADORA TESTE", cte.IssuerName);
        Assert.Equal("55555555000191", cte.PayerTaxId);
        Assert.Equal(1500.00m, cte.ServiceTotal);
        Assert.Equal(1500.00m, cte.AmountReceivable);
        Assert.Equal("00", cte.IcmsCst);
        Assert.Equal(105.00m, cte.IcmsValue);
        Assert.Equal("Frete de soja", cte.Observations);
        Assert.Equal(2, cte.TransportedKeys.Count);
        Assert.Equal("35260800000000000000550010000000011000000017", cte.TransportedKeys[0]);
        Assert.True(SupplierCteXmlReader.IsCte(Cte()));
    }

    [Fact]
    public void Payer_from_toma4() =>
        Assert.Equal("99999999000191",
            SupplierCteXmlReader.Read(Cte("<toma4><toma>4</toma><CNPJ>99999999000191</CNPJ></toma4>")).PayerTaxId);

    [Fact]
    public void Payer_from_toma3_sender() =>
        Assert.Equal("22222222000191", SupplierCteXmlReader.Read(Cte("<toma3><toma>0</toma></toma3>")).PayerTaxId);

    [Fact]
    public void Simples_nacional_icms_reads_as_SN()
    {
        var cte = SupplierCteXmlReader.Read(Cte(icmsXml: "<ICMSSN><indSN>1</indSN></ICMSSN>"));

        Assert.Equal("SN", cte.IcmsCst);
        Assert.Equal(0m, cte.IcmsValue);
    }

    [Fact]
    public void Nfe_xml_is_not_cte()
    {
        var nfe = Encoding.UTF8.GetBytes(
            """<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe1"/></NFe></nfeProc>""");

        Assert.False(SupplierCteXmlReader.IsCte(nfe));
        Assert.Throws<DefaultException>(() => SupplierCteXmlReader.Read(nfe));
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.False(SupplierCteXmlReader.IsCte(Encoding.UTF8.GetBytes("não é xml")));
        Assert.Equal("Arquivo não é um XML válido.",
            Assert.Throws<DefaultException>(() => SupplierCteXmlReader.Read(Encoding.UTF8.GetBytes("não é xml"))).Message);
        Assert.Equal("Arquivo XML vazio.",
            Assert.Throws<DefaultException>(() => SupplierCteXmlReader.Read([])).Message);
    }
}
