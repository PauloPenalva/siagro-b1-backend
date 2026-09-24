using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WeighingTickets;

/// <summary>
/// GAC-1191 — o ticket de pesagem calcula o Frete/Remoção como o romaneio, e passa método e
/// unitário ao romaneio que gera na conclusão.
/// </summary>
/// <remarks>
/// O peso bruto do ticket só existe depois da 2ª pesagem (<see cref="WeighingTicket.GrossWeight"/>
/// é derivado das duas leituras), mas o frete pode ser informado antes, na tela de edição. Por isso
/// o total é recalculado em todo ponto que muda o peso ou o frete: edição, 2ª pesagem e conclusão.
/// </remarks>
public class WeighingTicketFreightPriceTests
{
    private const string CardCode = "C0001";
    private const string ItemCode = "TRIGO";
    private const string WarehouseCode = "ARM01";
    private const string LotCode = "L-0001";
    private const string TruckCode = "ABC1D23";

    private sealed class CanTypeWeight : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(true);
        public Task<List<string>> GetAsync(string username) => Task.FromResult(new List<string>());
        public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(false);
    }

    private static async Task<IUnitOfWork> SeedLotAsync()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode,
            Description = "Lote comum",
            CardCode = CardCode,
            ItemCode = ItemCode,
            WarehouseCode = WarehouseCode,
            UoM = "KG",
            Nature = StorageAddressNature.Regular,
        });
        db.Context.Trucks.Add(new Truck { Code = TruckCode });

        await db.SaveChangesAsync();
        return db;
    }

    private static WeighingTicket NewReceiptTicket(WeighingTicketStage stage, int firstWeigh, int secondWeigh) => new()
    {
        Key = Guid.NewGuid(),
        Type = WeighingTicketType.Receipt,
        ItemCode = ItemCode,
        CardCode = CardCode,
        BranchCode = "01",
        TruckCode = TruckCode,
        TruckDriverCode = "1",
        Stage = stage,
        StorageAddressCode = LotCode,
        FirstWeighValue = firstWeigh,
        SecondWeighValue = secondWeigh,
    };

    private static WeighingTicketsCompletedService CompletedService(IUnitOfWork db) => new(
        db,
        new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
        new FakeItemService(new() { [ItemCode] = "Trigo" }),
        new StorageTransactionsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Trigo" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
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
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)),
        new FakeStringLocalizer<Resource>(),
        NullLogger<WeighingTicketsCompletedService>.Instance);

    /// <summary>
    /// 45.000 - 15.000 = 30.000 kg = 500 sacas × R$ 3,00 = R$ 1.500,00. O romaneio herda o MÉTODO e
    /// o UNITÁRIO, e não só o total — sem isso, o romaneio nasceria Fixo com unitário zero e a
    /// própria criação zeraria o frete.
    /// </summary>
    [Fact]
    public async Task Completing_a_per_bag_ticket_creates_a_transaction_with_the_same_freight()
    {
        var db = await SeedLotAsync();
        var ticket = NewReceiptTicket(WeighingTicketStage.ReadyForCompleting, 45_000, 15_000);
        ticket.FreightCalculationMethod = FreightCalculationMethod.PerBag;
        ticket.FreightUnitPrice = 3.00m;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await CompletedService(db).ExecuteAsync(ticket.Key, "tester");

        var transaction = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(FreightCalculationMethod.PerBag, transaction.FreightCalculationMethod);
        Assert.Equal(3.00m, transaction.FreightUnitPrice);
        Assert.Equal(1500.00m, transaction.FreightPrice);

        // O ticket concluído mostra o mesmo total que o romaneio que ele gerou.
        var completed = await db.Context.WeighingTickets.AsNoTracking().SingleAsync();
        Assert.Equal(1500.00m, completed.FreightPrice);
    }

    /// <summary>O caminho da Mhagro: valor da viagem, que não depende do peso.</summary>
    [Fact]
    public async Task Completing_a_fixed_ticket_keeps_the_trip_value_as_the_freight_total()
    {
        var db = await SeedLotAsync();
        var ticket = NewReceiptTicket(WeighingTicketStage.ReadyForCompleting, 45_000, 15_000);
        ticket.FreightCalculationMethod = FreightCalculationMethod.Fixed;
        ticket.FreightUnitPrice = 800m;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await CompletedService(db).ExecuteAsync(ticket.Key, "tester");

        var transaction = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(FreightCalculationMethod.Fixed, transaction.FreightCalculationMethod);
        Assert.Equal(800m, transaction.FreightPrice);
    }

    /// <summary>
    /// A tela Confirmar grava o frete por PATCH, com as duas pesagens já feitas: o total do ticket
    /// sai do peso bruto real.
    /// </summary>
    [Fact]
    public async Task Updating_a_weighed_ticket_recalculates_the_freight_total()
    {
        var db = await SeedLotAsync();
        var ticket = NewReceiptTicket(WeighingTicketStage.ReadyForCompleting, 45_000, 15_000);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var tracked = await db.Context.WeighingTickets.SingleAsync(x => x.Key == ticket.Key);
        tracked.FreightCalculationMethod = FreightCalculationMethod.PerBag;
        tracked.FreightUnitPrice = 3.00m;

        await new WeighingTicketsUpdateService(
                db,
                new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
                new FakeItemService(new() { [ItemCode] = "Trigo" }),
                NullLogger<WeighingTicketsUpdateService>.Instance)
            .ExecuteAsync(ticket.Key, tracked, "tester");

        var saved = await db.Context.WeighingTickets.AsNoTracking().SingleAsync();
        Assert.Equal(1500.00m, saved.FreightPrice);
    }

    /// <summary>
    /// Frete por saco informado na edição, ANTES da 2ª pesagem: o total nasce zero (não há peso
    /// bruto) e precisa ser refeito quando a 2ª pesagem fecha o peso — senão o detalhe do ticket
    /// mostra R$ 0,00 até alguém regravar a tela.
    /// </summary>
    [Fact]
    public async Task Second_weighing_recalculates_the_per_bag_freight_total()
    {
        var db = await SeedLotAsync();
        var ticket = NewReceiptTicket(WeighingTicketStage.ReadyForSecondWeighing, 45_000, 0);
        ticket.FreightCalculationMethod = FreightCalculationMethod.PerBag;
        ticket.FreightUnitPrice = 3.00m;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var validator = new WeighingCaptureValidator(
            db, new CanTypeWeight(), new CaptureStore(TimeSpan.FromMinutes(10)));

        await new WeighingTicketsSecondWeighingService(db, validator)
            .ExecuteAsync(ticket.Key, 15_000, null, "tester", captureId: null);

        var saved = await db.Context.WeighingTickets.AsNoTracking().SingleAsync();
        Assert.Equal(1500.00m, saved.FreightPrice);
    }
}
