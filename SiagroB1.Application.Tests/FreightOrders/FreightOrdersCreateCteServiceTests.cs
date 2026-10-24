using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightOrders;

public class FreightOrdersCreateCteServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly CtePartnerCatalog _partners = new CtePartnerCatalog()
        .Add("REM", "REMETENTE LTDA", "11222333000181", "3509502", "SP")
        .Add("DES", "DESTINATARIO LTDA", "44555666000172", "3550308", "SP")
        .Add("TOM", "TOMADOR LTDA", "77888999000163", "4106902", "PR");

    private FreightOrdersCreateCteService Create(string erp = "STANDALONE") =>
        new(_db, TestConfiguration.Erp(erp), new TransportDocumentDefaults(_db, _partners));

    private TransportDocumentsSaveService Save() =>
        new(_db, TestConfiguration.Erp("STANDALONE"), new TransportDocumentDefaults(_db, _partners),
            new TransportDocumentsTaxApplyService(_db, new UsageService(_db, NullLogger<UsageService>.Instance),
                new IbsCbsRatesService(_db)));

    private async Task<FreightOrder> SeedOrderAsync(Action<FreightOrder>? tweak = null)
    {
        await CteTestKeys.SeedBranchAndMunicipalitiesAsync(_db);
        var o = new FreightOrder
        {
            BranchCode = "01", Code = "OF-1", OrderDate = DateTime.Today, TakerCardCode = "TOM",
            SenderCardCode = "REM", RecipientCardCode = "DES", StartMunicipalityCode = "3509502", StartState = "SP",
            EndMunicipalityCode = "4106902", EndState = "PR", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS",
            ContractedWeightKg = 50_000m, PriceMethod = FreightCalculationMethod.PerTon, UnitPrice = 100m,
            ValidFrom = DateTime.Today.AddDays(-5), PaymentConditionCode = 7, UsageCode = null, CreatedBy = "t",
        };
        tweak?.Invoke(o);
        _db.Context.FreightOrders.Add(o);
        await _db.SaveChangesAsync();
        return o;
    }

    [Fact]
    public async Task CreateDraft_CopiesOrderData()
    {
        var o = await SeedOrderAsync();
        var key = await Create().CreateDraftAsync(o.Key, "u");

        var doc = await _db.Context.TransportDocuments.AsNoTracking().Include(d => d.Parties).SingleAsync(d => d.Key == key);
        Assert.Equal(CteStatus.Draft, doc.Status);
        Assert.Equal(o.Key, doc.FreightOrderKey);
        Assert.Equal("01", doc.BranchCode);
        Assert.Equal("SOJA EM GRAOS", doc.PredominantProduct);
        Assert.Equal(7, doc.PaymentConditionCode);
        Assert.Equal((int)CtePartyRole.OtherTaker, doc.TakerRole);
        Assert.Equal("3509502", doc.StartMunicipalityCode);
        Assert.Equal("PR", doc.EndState);
        Assert.Equal(3, doc.Parties.Count);
        Assert.Contains(doc.Parties, p => p.Role == CtePartyRole.OtherTaker && p.CardCode == "TOM");
        Assert.Empty(await _db.Context.TransportDocumentQuantities.ToListAsync());
    }

    [Fact]
    public async Task CreateDraft_TakerIsSender_UsesSenderRole()
    {
        var o = await SeedOrderAsync(x => x.TakerCardCode = "REM");
        var key = await Create().CreateDraftAsync(o.Key, "u");
        var doc = await _db.Context.TransportDocuments.AsNoTracking().Include(d => d.Parties).SingleAsync(d => d.Key == key);
        Assert.Equal((int)CtePartyRole.Sender, doc.TakerRole);
        Assert.Equal(2, doc.Parties.Count);
    }

    [Theory]
    [InlineData(FreightOrderStatus.Completed)]
    [InlineData(FreightOrderStatus.Cancelled)]
    public async Task CreateDraft_OrderNotOpenOrInExecution_Rejected(FreightOrderStatus status)
    {
        var o = await SeedOrderAsync(x => x.Status = status);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Create().CreateDraftAsync(o.Key, "u"));
        Assert.Equal("Só é possível emitir CT-e para ordem Aberta ou Em execução.", e.Message);
    }

    [Fact]
    public async Task CreateDraft_InExecution_Allowed_AndValidToTodayInclusive()
    {
        var o = await SeedOrderAsync(x => { x.Status = FreightOrderStatus.InExecution; x.ValidTo = DateTime.Today; });
        Assert.NotEqual(Guid.Empty, await Create().CreateDraftAsync(o.Key, "u"));
    }

    [Theory]
    [InlineData(-10, -1)]
    [InlineData(1, null)]
    public async Task CreateDraft_OutsideValidity_Rejected(int fromDays, int? toDays)
    {
        var o = await SeedOrderAsync(x =>
        {
            x.ValidFrom = DateTime.Today.AddDays(fromDays);
            x.ValidTo = toDays is { } d ? DateTime.Today.AddDays(d) : null;
        });
        var e = await Assert.ThrowsAsync<DefaultException>(() => Create().CreateDraftAsync(o.Key, "u"));
        Assert.Equal("Ordem de frete fora da vigência.", e.Message);
    }

    private static TransportDocumentInput Input(Guid orderKey, string takerCard) => new()
    {
        BranchCode = "01", Series = 1, TakerRole = 4, FreightOrderKey = orderKey,
        Parties = [new(CtePartyRole.Sender, "REM"), new(CtePartyRole.Recipient, "DES"), new(CtePartyRole.OtherTaker, takerCard)],
        NfeKeys = [CteTestKeys.Nfe(1)], PredominantProduct = "SOJA", CargoValue = 1000m,
        Quantities = [new("01", "PESO BRUTO", 30000m)], Components = [new("FRETE PESO", 650m)],
    };

    [Fact]
    public async Task CteSave_LinksOrderOfSameTaker()
    {
        var o = await SeedOrderAsync();
        var result = await Save().SaveAsync(Input(o.Key, "TOM"), "u");
        var doc = await _db.Context.TransportDocuments.AsNoTracking().SingleAsync(d => d.Key == result.Key);
        Assert.Equal(o.Key, doc.FreightOrderKey);
    }

    [Fact]
    public async Task CteSave_LinkOrderOfOtherTaker_Rejected()
    {
        var o = await SeedOrderAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Save().SaveAsync(Input(o.Key, "DES"), "u"));
        Assert.Equal("A ordem de frete é de outro tomador.", e.Message);
    }

    [Fact]
    public async Task CteSave_LinkClosedOrder_Rejected()
    {
        var o = await SeedOrderAsync(x => x.Status = FreightOrderStatus.Completed);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Save().SaveAsync(Input(o.Key, "TOM"), "u"));
        Assert.Equal("Só é possível emitir CT-e para ordem Aberta ou Em execução.", e.Message);
    }
}
