using System.Text;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Importação do XML do CT-e: rascunho de uma linha de serviço, filial pelo tomador e avisos.</summary>
public class PurchaseInvoicesImportCteTests
{
    private const string Key = "35260800000000000000570010000001231000000017";

    private static string CteXml(string tpCte = "0", string vRec = "1500.00", string payer = "55555555000191",
        string key = Key) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<cteProc xmlns="http://www.portalfiscal.inf.br/cte" versao="4.00">
  <CTe>
    <infCte Id="CTe{key}" versao="4.00">
      <ide><nCT>123</nCT><serie>1</serie><dhEmi>2026-08-06T09:30:00-03:00</dhEmi><tpCTe>{tpCte}</tpCTe>
        <xMunIni>SORRISO</xMunIni><UFIni>MT</UFIni><xMunFim>SANTOS</xMunFim><UFFim>SP</UFFim>
        <toma4><toma>4</toma><CNPJ>{payer}</CNPJ></toma4></ide>
      <compl><xObs>Frete de soja</xObs></compl>
      <emit><CNPJ>11111111000191</CNPJ><xNome>TRANSPORTADORA TESTE</xNome></emit>
      <vPrest><vTPrest>1500.00</vTPrest><vRec>{vRec}</vRec></vPrest>
      <imp><ICMS><ICMS00><CST>00</CST><vBC>1500.00</vBC><pICMS>7.00</pICMS><vICMS>105.00</vICMS></ICMS00></ICMS></imp>
      <infCTeNorm><infDoc>
        <infNFe><chave>35260800000000000000550010000000011000000017</chave></infNFe>
      </infDoc></infCTeNorm>
    </infCte>
  </CTe>
</cteProc>
""";

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static async Task<PurchaseInvoicesImportXmlService> ServiceAsync(string? freightItem = "FRETE")
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", ShortName = "MATRIZ",
            TaxId = "55.555.555/0001-91", FreightServiceItemCode = freightItem,
        });
        db.Context.Items.Add(new Item { ItemCode = "FRETE", ItemName = "FRETE SOBRE COMPRAS" });
        await db.Context.SaveChangesAsync();

        return new PurchaseInvoicesImportXmlService(new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { ["T0001"] = "TRANSPORTADORA" },
            taxIds: new Dictionary<string, string> { ["T0001"] = "11.111.111/0001-91" }), db);
    }

    [Fact]
    public async Task Cte_draft_has_carrier_branch_states_and_single_service_line()
    {
        var draft = await (await ServiceAsync()).ExecuteAsync(Bytes(CteXml()), "cte.xml");

        Assert.Equal(TaxDocumentKind.Cte, draft.TaxDocumentKind);
        Assert.Equal(PurchaseInvoiceType.Normal, draft.InvoiceType);
        Assert.Equal(Key, draft.ChaveNFe);
        Assert.Equal("T0001", draft.CardCode);
        Assert.Equal("T0001", draft.TruckingCompanyCode);
        Assert.Equal("TRANSPORTADORA TESTE", draft.CardName);
        Assert.Equal("01", draft.BranchCode);
        Assert.Equal("123", draft.TaxDocumentNumber);
        Assert.Equal("MT", draft.FreightStartState);
        Assert.Equal("SP", draft.FreightEndState);
        Assert.Equal("SORRISO", draft.FreightStartCity);
        Assert.Equal("SANTOS", draft.FreightEndCity);
        Assert.Equal("55555555000191", draft.FreightPayerTaxId);
        Assert.Equal("00", draft.CteIcmsCst);
        Assert.Equal(105.00m, draft.CteIcmsValue);
        Assert.Equal(1500.00m, draft.TotalDocumentValue);
        Assert.Equal("Frete de soja", draft.TaxPayerComments);
        Assert.Empty(draft.Warnings);

        var line = Assert.Single(draft.Items);
        Assert.Equal("FRETE", line.ItemCode);
        Assert.Equal("FRETE SOBRE COMPRAS", line.ItemName);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(1500.00m, line.UnitPrice);
        Assert.Null(line.UnitOfMeasureCode);
    }

    [Fact]
    public async Task Cte_with_other_type_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(
            async () => await (await ServiceAsync()).ExecuteAsync(Bytes(CteXml(tpCte: "1")), "cte.xml"));

        Assert.Equal("Só CT-e normal pode ser escriturado nesta versão (tpCTe 1).", ex.Message);
    }

    [Fact]
    public async Task Cte_with_unknown_payer_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(
            async () => await (await ServiceAsync()).ExecuteAsync(Bytes(CteXml(payer: "99999999000191")), "cte.xml"));

        Assert.Equal("O tomador do CT-e (CNPJ 99999999000191) não é uma filial cadastrada.", ex.Message);
    }

    [Fact]
    public async Task Cte_of_model_67_is_rejected()
    {
        var key67 = Key.Remove(20, 2).Insert(20, "67");

        var ex = await Assert.ThrowsAsync<DefaultException>(
            async () => await (await ServiceAsync()).ExecuteAsync(Bytes(CteXml(key: key67)), "cte.xml"));

        Assert.Equal("Só CT-e modelo 57 pode ser escriturado (o arquivo é modelo 67).", ex.Message);
    }

    [Fact]
    public async Task Cte_with_receivable_different_from_total_warns()
    {
        var draft = await (await ServiceAsync()).ExecuteAsync(Bytes(CteXml(vRec: "1400.00")), "cte.xml");

        Assert.Equal(
            ["O valor a receber do CT-e (R$ 1.400,00) difere do total da prestação (R$ 1.500,00); confira o título a pagar."],
            draft.Warnings);
    }

    [Fact]
    public async Task Branch_without_freight_item_warns_and_leaves_line_without_product()
    {
        var draft = await (await ServiceAsync(freightItem: null)).ExecuteAsync(Bytes(CteXml()), "cte.xml");

        Assert.Equal(["Filial sem produto padrão de frete: informe o produto da linha."], draft.Warnings);
        var line = Assert.Single(draft.Items);
        Assert.Null(line.ItemCode);
        Assert.Equal("SERVIÇO DE TRANSPORTE", line.ItemName);
    }

    [Fact]
    public async Task Nfe_import_is_unchanged()
    {
        const string nfe = """
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
  <NFe>
    <infNFe Id="NFe35260800000000000000550010000000011000000017">
      <ide><nNF>1</nNF><serie>1</serie><dhEmi>2026-08-05T10:00:00-03:00</dhEmi></ide>
      <emit><CNPJ>11111111000191</CNPJ><xNome>PRODUTOR TESTE</xNome></emit>
      <det nItem="1"><prod><cProd>SOJA</cProd><xProd>SOJA EM GRAOS</xProd><uCom>KG</uCom>
        <qCom>1000.000</qCom><vUnCom>1.5000</vUnCom></prod></det>
      <total><ICMSTot><vNF>1500.00</vNF></ICMSTot></total>
    </infNFe>
  </NFe>
</nfeProc>
""";

        var draft = await (await ServiceAsync()).ExecuteAsync(Bytes(nfe), "nfe.xml");

        Assert.Equal(TaxDocumentKind.Nfe, draft.TaxDocumentKind);
        Assert.Equal("T0001", draft.CardCode);
        Assert.Null(draft.BranchCode);
        Assert.Empty(draft.Warnings);
        var line = Assert.Single(draft.Items);
        Assert.Equal("SOJA", line.ItemCode);
        Assert.Equal("KG", line.UnitOfMeasureCode);
        Assert.Equal(1000m, line.Quantity);
    }
}
