using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.SpedFiscal;
using static SiagroB1.Application.Tests.SpedFiscal.SpedFiscalFileServiceTests;

namespace SiagroB1.Application.Tests.SpedFiscal;

public class SpedFiscalValidatorTests
{
    [Fact]
    public async Task Validate_MissingSettingsIsBlocking()
    {
        var db = await Seeded(settings: false);
        var data = await Loader(db).LoadAsync(Request());

        var issue = Assert.Single(SpedFiscalValidator.Validate(data));

        Assert.True(issue.Blocking);
        Assert.Equal("Parâmetros do SPED Fiscal não cadastrados para a filial.", issue.Message);
    }

    [Fact]
    public async Task Validate_CompleteDataHasNoIssues()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m));
        await Save(db);

        Assert.Empty(SpedFiscalValidator.Validate(await Loader(db).LoadAsync(Request())));
    }

    [Fact]
    public async Task Validate_BlocksMasterDataGaps()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(OwnSale("V1", 1000m, 120m));
        await Save(db);
        var data = await Loader(db).LoadAsync(Request());
        data.Branch.TaxId = null;
        data.Participants[0].MunicipalityCode = "";
        data.Participants[0].Cnpj = "";
        data.Lines[0].Cfop = "";
        data.Lines[0].CstIcms = "";
        data.MissingParticipantCodes.Add("X9");
        data.UnconvertibleUnits.Add(new SpedUnconvertibleUnit("10001", "SC"));
        data.Inventory = new FiscalInventoryData { Rows = [new FiscalInventoryRowDto { ItemCode = "10001", Quantity = -1m }] };

        var messages = SpedFiscalValidator.Validate(data).Select(i => i.Message).ToList();

        Assert.Equal(new[]
        {
            "Filial sem CNPJ.",
            "Parceiro X9 sem cadastro.",
            "Participante C001 sem CNPJ/CPF.",
            "Participante C001 sem município.",
            "Item 123/10001 sem CFOP.",
            "Item 123/10001 sem CST de ICMS.",
            "Produto 10001 sem fator de conversão para a unidade SC.",
            "Inventário com saldo negativo: 10001.",
        }, messages);
    }
}
