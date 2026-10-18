using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.DocNumbers;

/// <summary>
/// Criação e cópia de contratos entregam à numeração a série escolhida e o código digitado.
/// A cópia de um contrato de número manual nasce SEM código (número automático), senão cairia em
/// "número já em uso".
/// </summary>
public class DocumentCreateContractsTests
{
    private static readonly Guid SeriesKey = Guid.NewGuid();

    private static PurchaseContractsCreateService PurchaseCreate(UnitOfWork db, FakeDocNumberSequenceService fake) => new(
        db.Context,
        new FakeBusinessPartnerService(names: new() { ["F0001"] = "PRODUTOR" }),
        new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new() { [1] = "AGENTE" }),
        new FakeWarehouseService(new() { ["01"] = "ARM 01" }),
        fake,
        TestNotificationOutbox.For(db.Context),
        new FakeStringLocalizer<Resource>(),
        NullLogger<PurchaseContractsCreateService>.Instance);

    private static SalesContractsCreateService SalesCreate(UnitOfWork db, FakeDocNumberSequenceService fake) => new(
        db,
        new FakeBusinessPartnerService(names: new() { ["C0001"] = "CLIENTE" }),
        new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new() { [1] = "AGENTE" }),
        fake,
        TestNotificationOutbox.For(db.Context),
        NullLogger<SalesContractsCreateService>.Instance);

    private static PurchaseContract NewPurchase(string? code, Guid? key) => new()
    {
        Key = Guid.NewGuid(), Code = code, DocNumberKey = key,
        CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01", AgentCode = 1, TotalVolume = 1_000m, StandardPrice = 2m,
        Type = ContractType.Fixed,
    };

    private static SalesContract NewSales(string? code, Guid? key) => new()
    {
        Key = Guid.NewGuid(), Code = code, DocNumberKey = key,
        CardCode = "C0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
        AgentCode = 1, TotalVolume = 1_000m, Price = 2m, Type = ContractType.Fixed,
    };

    [Fact]
    public async Task PurchaseContractsCreate_UsesChosenSeriesAndTypedCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        db.Context.PurchaseContracts.Add(NewPurchase("USED", SeriesKey));
        await db.SaveChangesAsync();

        var contract = NewPurchase("PC-MANUAL", SeriesKey);
        await PurchaseCreate(db, fake).ExecuteAsync(contract, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.PurchaseContract, SeriesKey, "PC-MANUAL", 50, "USED");
        Assert.Equal("PC-MANUAL", contract.Code);
        Assert.Equal(SeriesKey, contract.DocNumberKey);
    }

    [Fact]
    public async Task PurchaseContractsCopy_ClearsCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        var original = NewPurchase("PC-MANUAL", SeriesKey);
        db.Context.PurchaseContracts.Add(original);
        await db.SaveChangesAsync();

        await new PurchaseContractsCopyService(db.Context, PurchaseCreate(db, fake), fake).ExecuteAsync(original.Key, "tester");

        var request = Assert.Single(fake.Requests);
        Assert.Null(request.TypedCode);
        Assert.Equal(SeriesKey, request.DocNumberKey);
    }

    [Fact]
    public async Task SalesContractsCreate_UsesChosenSeriesAndTypedCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        db.Context.SalesContracts.Add(NewSales("USED", SeriesKey));
        await db.SaveChangesAsync();

        var contract = NewSales("CV-MANUAL", SeriesKey);
        await SalesCreate(db, fake).ExecuteAsync(contract, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.SalesContract, SeriesKey, "CV-MANUAL", 50, "USED");
        Assert.Equal("CV-MANUAL", contract.Code);
    }

    [Fact]
    public async Task SalesContractsCopy_ClearsCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        var original = NewSales("CV-MANUAL", SeriesKey);
        db.Context.SalesContracts.Add(original);
        await db.SaveChangesAsync();

        await new SalesContractsCopyService(db.Context, SalesCreate(db, fake), fake).ExecuteAsync(original.Key, "tester");

        var request = Assert.Single(fake.Requests);
        Assert.Null(request.TypedCode);
    }
}
