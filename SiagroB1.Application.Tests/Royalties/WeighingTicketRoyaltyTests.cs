using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Royalties;

public class WeighingTicketRoyaltyTests
{
    private const string CardCode = "P0001";
    private const string OtherWarehouse = "P0099";
    private const string ItemCode = "SOJA";
    private const string LotCode = "LOTE-SOJA";
    private const string Warehouse = "ARM01";

    private static readonly Dictionary<string, string> Partners = new()
    {
        [CardCode] = "Produtor Teste",
        [OtherWarehouse] = "Armazém Parceiro",
    };

    // Mesma montagem de WeighingTicketTransshipmentLotExitTriggerTests.Service — confira os
    // usings/namespaces lá se algum não resolver.
    private static WeighingTicketsCompletedService Completed(IUnitOfWork db) => new(
        db,
        new FakeBusinessPartnerService(Partners),
        new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
        new StorageTransactionsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(Partners),
            new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            new FakeWarehouseService(new() { [Warehouse] = "Armazém" }),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance),
        new StorageTransactionsConfirmedService(
            db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance),
        new StorageAddressesGetService(db, NullLogger<StorageAddressesGetService>.Instance),
        new ShipmentLoadsTransshipmentAttachLotExitService(
            db,
            new FakeWarehouseService(new() { [Warehouse] = "Armazém" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)),
        new FakeStringLocalizer<Resource>(),
        NullLogger<WeighingTicketsCompletedService>.Instance);

    private static async Task<IUnitOfWork> SeedAsync(bool controlsRoyalties)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode, Description = "Lote soja", CardCode = CardCode, ItemCode = ItemCode,
            WarehouseCode = Warehouse, UoM = "KG", Nature = StorageAddressNature.Regular,
        });
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = ItemCode, ControlsRoyalties = controlsRoyalties });
        await db.SaveChangesAsync();
        return db;
    }

    private static WeighingTicket ReceiptTicket() => new()
    {
        Key = Guid.NewGuid(),
        Type = WeighingTicketType.Receipt,
        ItemCode = ItemCode,
        CardCode = CardCode,
        BranchCode = "01",
        TruckCode = "ABC1D23",
        TruckDriverCode = "1",
        Stage = WeighingTicketStage.ReadyForCompleting,
        StorageAddressCode = LotCode,
        FirstWeighValue = 50_000,
        SecondWeighValue = 20_000,
    };

    [Fact]
    public async Task Completing_requires_a_situation_when_the_item_controls_royalties()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Completed(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("situação de royalty", ex.Message);
    }

    [Fact]
    public async Task Completing_requires_the_test_code_when_tested()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.RoyaltySituation = RoyaltySituation.Tested;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Completed(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("código do teste", ex.Message);
    }

    [Fact]
    public async Task Completing_without_royalty_control_ignores_the_fields()
    {
        var db = await SeedAsync(controlsRoyalties: false);
        var ticket = ReceiptTicket();
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Completed(db).ExecuteAsync(ticket.Key, "tester");

        var st = await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.WeighingTicketKey == ticket.Key);
        Assert.Null(st.RoyaltySituation);
    }

    [Fact]
    public async Task Completing_copies_the_royalty_fields_and_defaults_the_origin_to_the_partner()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.RoyaltySituation = RoyaltySituation.Tested;
        ticket.RoyaltyTestCode = "T-123";
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Completed(db).ExecuteAsync(ticket.Key, "tester");

        var st = await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.WeighingTicketKey == ticket.Key);
        Assert.Equal(RoyaltySituation.Tested, st.RoyaltySituation);
        Assert.Equal("T-123", st.RoyaltyTestCode);
        Assert.Equal(CardCode, st.RoyaltyOriginCode);
        Assert.Equal("Produtor Teste", st.RoyaltyOriginName);
    }

    [Fact]
    public async Task Update_resolves_the_origin_name_and_clears_the_test_code_outside_tested()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.Stage = WeighingTicketStage.ReadyForFirstWeighing;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var patch = ReceiptTicket();
        patch.Key = ticket.Key;
        patch.Stage = ticket.Stage;
        patch.RoyaltySituation = RoyaltySituation.ParticipantWarehouse;
        patch.RoyaltyTestCode = "sobrou";
        patch.RoyaltyOriginCode = OtherWarehouse;

        var service = new WeighingTicketsUpdateService(
            db, new FakeBusinessPartnerService(Partners), new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            NullLogger<WeighingTicketsUpdateService>.Instance);
        await service.ExecuteAsync(ticket.Key, patch, "tester");

        var saved = await db.Context.WeighingTickets.AsNoTracking().SingleAsync(x => x.Key == ticket.Key);
        Assert.Equal("Armazém Parceiro", saved.RoyaltyOriginName);
        Assert.Null(saved.RoyaltyTestCode);
    }
}
