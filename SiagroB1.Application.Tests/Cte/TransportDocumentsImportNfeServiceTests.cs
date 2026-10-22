using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Importação do XML da NF-e transportada para o rascunho do CT-e.</summary>
public class TransportDocumentsImportNfeServiceTests
{
    private const string SenderCnpj = "11222333000181";
    private const string RecipientCnpj = "44555666000172";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly CtePartnerCatalog _partners = new CtePartnerCatalog()
        .Add("REM", "REMETENTE LTDA", SenderCnpj, "3509502", "SP")
        .Add("DES", "DESTINATARIO LTDA", RecipientCnpj, "4106902", "PR");

    private TransportDocumentsImportNfeService Service() =>
        new(_db, TaxTestServices.Config("STANDALONE"), new TransportDocumentDefaults(_db, _partners),
            new TransportDocumentsTaxApplyService(_db, new UsageService(_db, NullLogger<UsageService>.Instance),
                new IbsCbsRatesService(_db)), _partners);

    /// <summary>NF-e sintética (dados fictícios) com emitente, destinatário, itens e peso bruto.</summary>
    internal static string Xml(int number, decimal total, string emitter = SenderCnpj, string recipient = RecipientCnpj,
        decimal grossWeight = 30000m, string model = "55") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\"><NFe>" +
        $"<infNFe Id=\"NFe{CteTestKeys.Nfe(number, emitter)}\" versao=\"4.00\"><ide><cUF>35</cUF><mod>{model}</mod><serie>1</serie><nNF>{number}</nNF></ide>" +
        $"<emit><CNPJ>{emitter}</CNPJ><xNome>EMITENTE</xNome><enderEmit><cMun>3509502</cMun><UF>SP</UF></enderEmit></emit>" +
        $"<dest><CNPJ>{recipient}</CNPJ><xNome>DESTINATARIO</xNome><enderDest><cMun>4106902</cMun><UF>PR</UF></enderDest></dest>" +
        "<det nItem=\"1\"><prod><cProd>1</cProd><xProd>MILHO EM GRAOS</xProd><vProd>100.00</vProd></prod></det>" +
        $"<det nItem=\"2\"><prod><cProd>2</cProd><xProd>SOJA EM GRAOS</xProd><vProd>{(total - 100m).ToString("0.00", CultureInfo.InvariantCulture)}</vProd></prod></det>" +
        $"<total><ICMSTot><vNF>{total.ToString("0.00", CultureInfo.InvariantCulture)}</vNF></ICMSTot></total>" +
        $"<transp><modFrete>0</modFrete><vol><qVol>1</qVol><pesoB>{grossWeight.ToString("0.000", CultureInfo.InvariantCulture)}</pesoB></vol></transp>" +
        "</infNFe></NFe></nfeProc>";

    private async Task<Domain.Entities.TransportDocument> LoadAsync(Guid key) =>
        await _db.Context.TransportDocuments.AsNoTracking()
            .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities)
            .SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Import_fills_parties_key_value_and_product()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db, defaultUnit: "01");

        var key = await Service().ImportAsync(null, "01", [Xml(1, 5000m)], "tester");

        var doc = await LoadAsync(key);
        Assert.Equal("REM", doc.Parties.Single(p => p.Role == CtePartyRole.Sender).CardCode);
        Assert.Equal("DES", doc.Parties.Single(p => p.Role == CtePartyRole.Recipient).CardCode);
        Assert.Equal(CteTestKeys.Nfe(1, SenderCnpj), doc.NfeKeys.Single().AccessKey);
        Assert.Equal(5000m, doc.CargoValue);
        Assert.Equal("SOJA EM GRAOS", doc.PredominantProduct);
        var quantity = doc.Quantities.Single();
        Assert.Equal("01", quantity.UnitCode);
        Assert.Equal(30000m, quantity.Quantity);
        Assert.Equal("3509502", doc.StartMunicipalityCode);
        Assert.Equal("4106902", doc.EndMunicipalityCode);
        Assert.Equal(CteStatus.Draft, doc.Status);
    }

    [Fact]
    public async Task Import_with_unknown_partner_lists_missing_tax_ids_and_creates_nothing()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().ImportAsync(null, "01", [Xml(1, 5000m, "99888777000166", "66555444000133")], "tester"));

        Assert.Equal("Cadastre os parceiros antes de importar: 99888777000166, 66555444000133.", ex.Message);
        Assert.Empty(_db.Context.TransportDocuments);
    }

    [Fact]
    public async Task Import_two_nfes_sums_cargo_value_and_keeps_two_keys()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db, defaultUnit: "01");

        var key = await Service().ImportAsync(null, "01", [Xml(1, 5000m), Xml(2, 3000m, grossWeight: 10000m)], "tester");

        var doc = await LoadAsync(key);
        Assert.Equal(8000m, doc.CargoValue);
        Assert.Equal(2, doc.NfeKeys.Count);
        Assert.Equal(40000m, doc.Quantities.Single().Quantity);
    }

    [Fact]
    public async Task Import_complements_existing_draft()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db, defaultUnit: "01");
        var key = await Service().ImportAsync(null, "01", [Xml(1, 5000m)], "tester");

        var same = await Service().ImportAsync(key, "01", [Xml(2, 3000m)], "tester");

        var doc = await LoadAsync(same);
        Assert.Equal(key, same);
        Assert.Equal(2, doc.NfeKeys.Count);
        Assert.Equal(8000m, doc.CargoValue);
        Assert.Equal(2, doc.Parties.Count);
    }

    [Fact]
    public async Task Import_uses_branch_default_unit_m3_with_zero_quantity()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db, defaultUnit: "00");

        var key = await Service().ImportAsync(null, "01", [Xml(1, 5000m)], "tester");

        var quantity = (await LoadAsync(key)).Quantities.Single();
        Assert.Equal("00", quantity.UnitCode);
        Assert.Equal(0m, quantity.Quantity);
    }

    [Fact]
    public void Reader_refuses_non_nfe_model()
    {
        var ex = Assert.Throws<DefaultException>(() => NfeXmlForCteReader.Read(Xml(1, 5000m, model: "65")));

        Assert.Equal(NfeXmlForCteReader.NotNfeMessage, ex.Message);
    }
}
