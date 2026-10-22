using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>ICMS do CT-e pela natureza, com as UFs da prestação (início → fim).</summary>
public class TransportDocumentsTaxApplyServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<int> SeedAsync()
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var usage = await new UsageService(_db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "PRESTACAO DE SERVICO DE TRANSPORTE", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5352", CfopOutgoingOutState = "6352",
            IcmsInStateCst = "00", IcmsInStateRate = 12m, IcmsOutStateCst = "00", PisCst = "07", CofinsCst = "07",
        });
        await _db.SaveChangesAsync();
        return usage.Code;
    }

    private TransportDocumentsTaxApplyService Service() =>
        new(_db, new UsageService(_db, NullLogger<UsageService>.Instance), new IbsCbsRatesService(_db));

    private static TransportDocument Doc(int usage, string start, string end)
    {
        var doc = new TransportDocument { BranchCode = "01", UsageCode = usage, StartState = start, EndState = end };
        doc.Components.Add(new TransportDocumentComponent { Name = "FRETE PESO", Value = 650m });
        return doc;
    }

    [Fact]
    public async Task Internal_sp_uses_in_state_cfop_and_12_percent()
    {
        var usage = await SeedAsync();
        var doc = Doc(usage, "SP", "SP");

        await Service().ApplyAsync(doc);

        Assert.Equal("5352", doc.Cfop);
        Assert.Equal(12m, doc.IcmsRate);
        Assert.Equal("00", doc.IcmsCst);
        Assert.Equal(650m, doc.IcmsBase);
        Assert.Equal(78m, doc.IcmsValue);
        Assert.Equal("PRESTACAO DE SERVICO DE TRANSPORTE", doc.NatureText);
    }

    [Fact]
    public async Task Interstate_uses_out_of_state_cfop_and_interstate_rate()
    {
        var usage = await SeedAsync();
        var doc = Doc(usage, "SP", "PR");

        await Service().ApplyAsync(doc);

        // Prestação interestadual: CFOP 6xxx mesmo começando na UF da filial (o CT-e recusa 5xxx com UF início ≠ fim).
        Assert.Equal("6352", doc.Cfop);
        Assert.Equal(12m, doc.IcmsRate);
    }

    [Fact]
    public async Task Start_outside_branch_state_uses_out_of_state_cfop()
    {
        var usage = await SeedAsync();
        var doc = Doc(usage, "PR", "SP");

        await Service().ApplyAsync(doc);

        Assert.Equal("6352", doc.Cfop);
        Assert.Equal(12m, doc.IcmsRate);
    }

    [Fact]
    public async Task Interstate_from_south_to_north_uses_7_percent()
    {
        var usage = await SeedAsync();
        var doc = Doc(usage, "PR", "BA");

        await Service().ApplyAsync(doc);

        Assert.Equal(7m, doc.IcmsRate);
        Assert.Equal(45.5m, doc.IcmsValue);
    }
}
