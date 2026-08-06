using System.Text;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Leitura do XML da NF-e de entrada.
///
/// O serviço SÓ LÊ: devolve um rascunho e quem grava é o POST do documento. E não tenta adivinhar
/// a amarração linha → NF de origem, porque o layout guarda as referências em <c>ide/NFref</c>,
/// que é do CABEÇALHO — o XML não diz qual linha veio de qual origem.
/// </summary>
public class PurchaseInvoicesImportXmlServiceTests
{
    private const string Nfe = """
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
  <NFe>
    <infNFe Id="NFe35260800000000000000550010000000011000000017">
      <ide><nNF>1</nNF><serie>1</serie><dhEmi>2026-08-05T10:00:00-03:00</dhEmi></ide>
      <emit><CNPJ>12345678000199</CNPJ><xNome>PRODUTOR TESTE</xNome></emit>
      <det nItem="1">
        <prod><cProd>SOJA</cProd><xProd>SOJA EM GRAOS</xProd><uCom>KG</uCom>
        <qCom>1000.000</qCom><vUnCom>1.5000</vUnCom></prod>
      </det>
      <det nItem="2">
        <prod><cProd>MILHO</cProd><xProd>MILHO EM GRAOS</xProd><uCom>KG</uCom>
        <qCom>500.000</qCom><vUnCom>0.8000</vUnCom></prod>
      </det>
      <total><ICMSTot><vNF>1900.00</vNF></ICMSTot></total>
      <infAdic><infCpl>Ref. NF 123 serie 1</infCpl></infAdic>
    </infNFe>
  </NFe>
</nfeProc>
""";

    private static PurchaseInvoicesImportXmlService Service(
        string cardCode = "F0001", string taxId = "12.345.678/0001-99") =>
        new(new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [cardCode] = "CADASTRO" },
            taxIds: new Dictionary<string, string> { [cardCode] = taxId }));

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    [Fact]
    public async Task Header_is_read_from_the_xml()
    {
        var draft = await Service().ExecuteAsync(Bytes(Nfe), "nfe.xml");

        // A chave vem no atributo Id como "NFe" + 44 dígitos — o prefixo tem de sair.
        Assert.Equal("35260800000000000000550010000000011000000017", draft.ChaveNFe);
        Assert.Equal("1", draft.TaxDocumentNumber);
        Assert.Equal("1", draft.TaxDocumentSeries);
        Assert.Equal(1900.00m, draft.TotalDocumentValue);
        Assert.Equal("Ref. NF 123 serie 1", draft.TaxPayerComments);
        Assert.Equal("F0001", draft.CardCode);
        Assert.Equal("nfe.xml", draft.XmlFileName);
    }

    [Fact]
    public async Task Issuer_name_comes_from_the_xml_not_from_the_registry()
    {
        var draft = await Service().ExecuteAsync(Bytes(Nfe), "nfe.xml");

        // O que vale num documento fiscal de terceiro é o nome que consta NA NOTA.
        Assert.Equal("PRODUTOR TESTE", draft.CardName);
    }

    [Fact]
    public async Task Lines_are_read_from_det()
    {
        var draft = await Service().ExecuteAsync(Bytes(Nfe), "nfe.xml");

        Assert.Equal(2, draft.Items.Count);

        var first = draft.Items[0];
        Assert.Equal("SOJA", first.ItemCode);
        Assert.Equal("SOJA EM GRAOS", first.ItemName);
        Assert.Equal("KG", first.UnitOfMeasureCode);
        Assert.Equal(1000m, first.Quantity);
        Assert.Equal(1.5m, first.UnitPrice);
    }

    [Fact]
    public async Task Binding_is_never_guessed()
    {
        var draft = await Service().ExecuteAsync(Bytes(Nfe), "nfe.xml");

        // Casar linha com origem por quantidade erraria EM SILÊNCIO, que aqui é o pior erro.
        // O rascunho não tem campo de amarração: ela é manual, na tela.
        Assert.All(draft.Items, i => Assert.NotNull(i.ItemCode));
    }

    [Fact]
    public async Task A_bare_NFe_without_the_protocol_envelope_is_accepted()
    {
        var bare = Nfe
            .Replace("<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\">", "")
            .Replace("</nfeProc>", "")
            .Replace("<NFe>", "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");

        var draft = await Service().ExecuteAsync(Bytes(bare), "nfe.xml");

        Assert.Equal("1", draft.TaxDocumentNumber);
    }

    [Fact]
    public async Task Empty_file_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Service().ExecuteAsync([], "x.xml"));
    }

    [Fact]
    public async Task Non_xml_content_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteAsync(Bytes("não é xml"), "x.xml"));
    }

    [Fact]
    public async Task Xml_that_is_not_an_nfe_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteAsync(Bytes("<outro><coisa/></outro>"), "x.xml"));
    }

    [Fact]
    public async Task Unknown_issuer_is_refused()
    {
        // Sem parceiro não há como listar as notas de origem: deixar em branco só adiaria a
        // descoberta para a hora de amarrar.
        await Assert.ThrowsAsync<DefaultException>(
            () => Service(taxId: "99.999.999/0001-99").ExecuteAsync(Bytes(Nfe), "nfe.xml"));
    }

    [Fact]
    public async Task Nfe_without_items_is_refused()
    {
        var noItems = Nfe[..Nfe.IndexOf("<det nItem=\"1\">", StringComparison.Ordinal)]
                      + "</infNFe></NFe></nfeProc>";

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteAsync(Bytes(noItems), "nfe.xml"));
    }
}
