using System.Text;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Commons.Resources;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Application.Services;
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
            taxIds: new Dictionary<string, string> { [cardCode] = taxId }),
            SiagroB1.Application.Tests.Support.TestDb.CreateUnitOfWork());

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private sealed class PassthroughLocalizer : IStringLocalizer<Resource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>Serviço REAL sobre o banco EF — é o que roda em STANDALONE (o Fake é em memória).</summary>
    private static async Task<PurchaseInvoicesImportXmlService> RealServiceAsync(string? maskedTaxId)
    {
        var db = SiagroB1.Application.Tests.Support.TestDb.CreateUnitOfWork();
        if (maskedTaxId != null)
        {
            db.Context.BusinessPartners.Add(new BusinessPartner
            {
                CardCode = "F0009", CardName = "FORNECEDOR REAL", TaxId = maskedTaxId
            });
            await db.Context.SaveChangesAsync();
        }
        var partners = new BusinessPartnerService(
            db, NullLogger<BusinessPartnerService>.Instance, new PassthroughLocalizer());
        return new PurchaseInvoicesImportXmlService(partners, db);
    }

    private static string NfeFor(string cnpj) => Nfe.Replace("12345678000199", cnpj);

    [Fact]
    public async Task Issuer_is_found_by_digits_when_registry_is_masked_through_the_real_service()
    {
        var service = await RealServiceAsync("11.222.333/0001-81");

        var draft = await service.ExecuteAsync(Bytes(NfeFor("11222333000181")), "nfe.xml");

        Assert.Equal("F0009", draft.CardCode);
    }

    [Fact]
    public async Task Unregistered_issuer_is_a_business_error_through_the_real_service()
    {
        var service = await RealServiceAsync("99.999.999/0001-99");

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => service.ExecuteAsync(Bytes(NfeFor("11222333000181")), "nfe.xml"));

        Assert.StartsWith("Nenhum parceiro cadastrado", ex.Message);
    }

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

    [Fact]
    public async Task Draft_lines_carry_the_supplier_item_number_in_xml_order()
    {
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
            taxIds: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "529.982.247-25" });
        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m)),
            SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms51(500m)));

        var draft = await new PurchaseInvoicesImportXmlService(partners, SiagroB1.Application.Tests.Support.TestDb.CreateUnitOfWork()).ExecuteAsync(SupplierNfeXml.Bytes(xml), "nota.xml");

        Assert.Equal(PurchaseNfeTestSeed.Supplier, draft.CardCode);
        Assert.Equal(SupplierNfeXml.AccessKey, draft.ChaveNFe);
        Assert.Equal(new[] { (1, "TRG", 1000m), (2, "MLH", 500m) },
            draft.Items.Select(i => (i.NfeItemNumber!.Value, i.ItemCode!, i.Quantity)).ToArray());
    }

    [Fact]
    public async Task Draft_lines_carry_the_line_charges_of_the_xml()
    {
        var xml = SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m),
                charges: SupplierNfeXml.Charges(100m, 20m, 50m, 30m)),
            SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

        var draft = await Service(PurchaseNfeTestSeed.Supplier, "529.982.247-25").ExecuteAsync(SupplierNfeXml.Bytes(xml), "nfe.xml");

        var (first, second) = (draft.Items[0], draft.Items[1]);
        Assert.Equal((100m, 20m, 50m, 30m), (first.FreightValue, first.InsuranceValue, first.DiscountValue, first.OtherExpensesValue));
        Assert.Equal((0m, 0m, 0m, 0m), (second.FreightValue, second.InsuranceValue, second.DiscountValue, second.OtherExpensesValue));
    }

    [Fact]
    public async Task Complement_nfe_finnfe_2_becomes_a_value_complement_draft_with_the_line_value()
    {
        // NF-e de complemento (finNFe=2): qCom/vUnCom zerados e o valor só em vProd.
        var xml = Nfe.Replace("<ide>", "<ide><finNFe>2</finNFe>")
            .Replace("<qCom>1000.000</qCom><vUnCom>1.5000</vUnCom>", "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>271.67</vProd>")
            .Replace("<qCom>500.000</qCom><vUnCom>0.8000</vUnCom>", "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>100.00</vProd>");

        var draft = await Service().ExecuteAsync(Bytes(xml), "nfe.xml");

        Assert.Equal(SiagroB1.Domain.Enums.PurchaseInvoiceType.ValueComplement, draft.InvoiceType);
        Assert.Equal(new[] { 271.67m, 100m }, draft.Items.Select(i => i.ComplementAmount));
        Assert.All(draft.Items, i => Assert.Equal((0m, 0m), (i.Quantity, i.UnitPrice)));
    }

    [Fact]
    public async Task Normal_nfe_keeps_quantity_and_price_and_no_complement_amount()
    {
        var draft = await Service().ExecuteAsync(Bytes(Nfe), "nfe.xml");

        Assert.Equal(SiagroB1.Domain.Enums.PurchaseInvoiceType.Normal, draft.InvoiceType);
        Assert.All(draft.Items, i => Assert.Equal(0m, i.ComplementAmount));
        Assert.Equal((1000m, 1.5m), (draft.Items[0].Quantity, draft.Items[0].UnitPrice));
    }

    /// <summary>NF-e finNFe 2 só de ICMS: vProd zerado e o imposto em vBC/vICMS (spec 2026-10-17 D7).</summary>
    private static string IcmsComplementXml() => Nfe.Replace("<ide>", "<ide><finNFe>2</finNFe>")
        .Replace("<qCom>1000.000</qCom><vUnCom>1.5000</vUnCom></prod>",
            "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>0.00</vProd></prod>"
            + "<imposto><ICMS><ICMS90><CST>90</CST><vBC>1000.00</vBC><pICMS>12.00</pICMS><vICMS>120.00</vICMS></ICMS90></ICMS></imposto>")
        .Replace("<qCom>500.000</qCom><vUnCom>0.8000</vUnCom></prod>",
            "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>0.00</vProd></prod>"
            + "<imposto><ICMS><ICMS90><CST>90</CST><vBC>400.00</vBC><pICMS>12.00</pICMS><vICMS>48.00</vICMS></ICMS90></ICMS></imposto>");

    [Fact]
    public async Task Icms_complement_xml_imports_base_and_value()
    {
        var draft = await Service().ExecuteAsync(Bytes(IcmsComplementXml()), "nfe.xml");

        Assert.Equal(SiagroB1.Domain.Enums.PurchaseInvoiceType.IcmsComplement, draft.InvoiceType);
        Assert.All(draft.Items, i => Assert.Equal((0m, 0m, 0m), (i.Quantity, i.UnitPrice, i.ComplementAmount)));
        Assert.Equal(new[] { (1000m, 120m), (400m, 48m) }, draft.Items.Select(i => (i.IcmsBase, i.IcmsValue)));
    }

    [Fact]
    public async Task Value_complement_xml_still_imports_as_value()
    {
        // Regressão: itens com vProd > 0 (mesmo com ICMS destacado) continuam sendo complemento de valor.
        var xml = Nfe.Replace("<ide>", "<ide><finNFe>2</finNFe>")
            .Replace("<qCom>1000.000</qCom><vUnCom>1.5000</vUnCom></prod>",
                "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>271.67</vProd></prod>"
                + "<imposto><ICMS><ICMS90><CST>90</CST><vBC>271.67</vBC><vICMS>32.60</vICMS></ICMS90></ICMS></imposto>")
            .Replace("<qCom>500.000</qCom><vUnCom>0.8000</vUnCom></prod>",
                "<qCom>0.0000</qCom><vUnCom>0.0000000000</vUnCom><vProd>100.00</vProd></prod>");

        var draft = await Service().ExecuteAsync(Bytes(xml), "nfe.xml");

        Assert.Equal(SiagroB1.Domain.Enums.PurchaseInvoiceType.ValueComplement, draft.InvoiceType);
        Assert.Equal(new[] { 271.67m, 100m }, draft.Items.Select(i => i.ComplementAmount));
    }

    [Fact]
    public async Task Mixed_items_complement_xml_is_a_value_complement()
    {
        // Um item só de ICMS e outro com valor: não é complemento de ICMS (todo item precisa ser).
        var xml = IcmsComplementXml().Replace(
            "<vProd>0.00</vProd></prod><imposto><ICMS><ICMS90><CST>90</CST><vBC>400.00</vBC>",
            "<vProd>50.00</vProd></prod><imposto><ICMS><ICMS90><CST>90</CST><vBC>400.00</vBC>");

        var draft = await Service().ExecuteAsync(Bytes(xml), "nfe.xml");

        Assert.Equal(SiagroB1.Domain.Enums.PurchaseInvoiceType.ValueComplement, draft.InvoiceType);
    }

    [Fact]
    public async Task Imported_icms_complement_survives_create()
    {
        // Review Focus 3: o rascunho do import, gravado pelo create com o motor ativo, mantém base e valor do XML.
        var draft = await Service().ExecuteAsync(Bytes(IcmsComplementXml()), "nfe.xml");
        var first = draft.Items[0];
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var usage = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = draft.InvoiceType,
            TaxDocumentKind = TaxDocumentKind.Other, TaxDocumentNumber = draft.TaxDocumentNumber,
            TaxDocumentSeries = draft.TaxDocumentSeries, IssueDate = draft.IssueDate ?? new DateTime(2026, 8, 5),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = first.ItemName, UnitOfMeasureCode = first.UnitOfMeasureCode,
            Quantity = first.Quantity, UnitPrice = first.UnitPrice, ComplementAmount = first.ComplementAmount,
            IcmsBase = first.IcmsBase, IcmsValue = first.IcmsValue, UsageCode = usage,
        });

        await new PurchaseInvoicesCreateService(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()), new FakeDocNumberSequenceService())
            .ExecuteAsync(invoice, "ana");

        db.Context.ChangeTracker.Clear();
        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal((1000m, 120m), (line.IcmsBase, line.IcmsValue));
    }
}
