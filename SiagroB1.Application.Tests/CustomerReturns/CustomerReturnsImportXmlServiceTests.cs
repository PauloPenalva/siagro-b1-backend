using System.Text;
using SiagroB1.Application.Services.CustomerReturns;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.CustomerReturns;

/// <summary>
/// Importação do XML da NF-e de devolução do cliente. O XML economiza a digitação do cabeçalho
/// e das linhas; a amarração com a NF de origem continua manual, porque o layout não a carrega.
/// </summary>
public class CustomerReturnsImportXmlServiceTests
{
    private const string Cnpj = "06315338017780";

    private static string Xml(string cnpj = Cnpj, string infCpl =
        "DEVOLUCAO REF. NF 000002361 SERIE 1 CHAVE 4126... QTDE 20,000") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
          <NFe>
            <infNFe Id="NFe41260806315338017780550010000012341000012349" versao="4.00">
              <ide>
                <nNF>1234</nNF>
                <serie>1</serie>
                <dhEmi>2026-08-04T10:15:00-03:00</dhEmi>
              </ide>
              <emit>
                <CNPJ>{cnpj}</CNPJ>
                <xNome>COFCO INTERNATIONAL BRASIL S.A.</xNome>
              </emit>
              <det nItem="1">
                <prod>
                  <cProd>MILHO</cProd>
                  <xProd>MILHO EM GRAOS</xProd>
                  <qCom>20.0000</qCom>
                  <vUnCom>1.5000000000</vUnCom>
                </prod>
              </det>
              <det nItem="2">
                <prod>
                  <cProd>MILHO</cProd>
                  <xProd>MILHO EM GRAOS</xProd>
                  <qCom>13.5000</qCom>
                  <vUnCom>1.5000000000</vUnCom>
                </prod>
              </det>
              <total>
                <ICMSTot>
                  <vNF>50.25</vNF>
                </ICMSTot>
              </total>
              <infAdic>
                <infCpl>{infCpl}</infCpl>
              </infAdic>
            </infNFe>
          </NFe>
        </nfeProc>
        """;

    private static CustomerReturnsImportXmlService Service(string partnerTaxId = Cnpj) =>
        new(new FakeBusinessPartnerService(
            names: new() { ["C024573"] = "COFCO INTERNATIONAL BRASIL S.A." },
            taxIds: new() { ["C024573"] = partnerTaxId }));

    [Fact]
    public async Task Imports_header_and_lines_from_the_xml()
    {
        var result = await Service().ExecuteAsync(Encoding.UTF8.GetBytes(Xml()), "devolucao.xml");

        Assert.Equal("41260806315338017780550010000012341000012349", result.AccessKey);
        Assert.Equal("1234", result.DocumentNumber);
        Assert.Equal("1", result.DocumentSeries);
        Assert.Equal(50.25m, result.TotalValue);
        Assert.Equal("C024573", result.CardCode);
        Assert.Equal(new DateTime(2026, 8, 4), result.IssueDate!.Value.Date);
        Assert.Equal(2, result.Items.Count);

        var first = result.Items.First();
        Assert.Equal("MILHO", first.ItemCode);
        Assert.Equal(20m, first.Quantity);
        Assert.Equal(1.5m, first.UnitPrice);

        // Nenhuma linha nasce amarrada: o XML não carrega esse vínculo.
        Assert.All(result.Items, i => Assert.Null(i.SalesInvoiceItemKey));
    }

    [Fact]
    public async Task Keeps_the_taxpayer_comments_that_the_operator_uses_to_bind()
    {
        var result = await Service().ExecuteAsync(Encoding.UTF8.GetBytes(Xml()), "devolucao.xml");

        Assert.Contains("NF 000002361", result.TaxPayerComments);
        Assert.Contains("QTDE 20,000", result.TaxPayerComments);
    }

    [Fact]
    public async Task Keeps_the_original_xml()
    {
        var bytes = Encoding.UTF8.GetBytes(Xml());

        var result = await Service().ExecuteAsync(bytes, "devolucao.xml");

        Assert.Equal(bytes, result.XmlData);
        Assert.Equal("devolucao.xml", result.XmlFileName);
    }

    [Fact]
    public async Task Unknown_issuer_is_a_business_error()
    {
        // Emitente que não está no cadastro: sem cliente não há como listar as origens.
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(partnerTaxId: "99999999999999")
                .ExecuteAsync(Encoding.UTF8.GetBytes(Xml()), "devolucao.xml"));

        Assert.Contains("Nenhum parceiro cadastrado", ex.Message);
    }

    [Fact]
    public async Task Invalid_file_is_a_business_error()
    {
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service().ExecuteAsync(Encoding.UTF8.GetBytes("isto não é xml"), "x.xml"));
    }
}
