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

namespace SiagroB1.Application.Tests.WeighingTickets;

/// <summary>
/// GAC-1181 fase 2, Task 2 — teste de caracterização. O transbordo em armazém próprio vai decidir,
/// em tasks futuras, com base na natureza do lote que o romaneio de pesagem carrega
/// (<see cref="StorageAddressNature"/>, Task 1). Esta classe não muda comportamento nenhum: prova
/// que <see cref="WeighingTicketsCompletedService"/> já preserva o lote (e, por tabela, a
/// natureza dele) no romaneio (<see cref="StorageTransaction"/>) que nasce ao completar o ticket —
/// tanto na entrada (Receipt) quanto na saída (Shipment). O cenário usa armazém próprio
/// (<see cref="WarehouseComplement.IsOwn"/>) porque é o caso real do transbordo, mesmo que a trava
/// de <c>StorageAddressesCreateService</c> não entre em jogo aqui (o lote é persistido direto no
/// contexto InMemory).
/// </summary>
/// <remarks>
/// ⚠️ Ajustado pelo redesenho do GAC-1181 fase 2 (2026-09-23): a confirmação de uma saída
/// (<c>Shipment</c>) num lote de natureza Transbordo agora EXIGE um transbordo aberto do MESMO
/// caminhão (<see cref="ShipmentLoadTransshipmentRules.ResolveOpenTransshipmentForLotExitAsync"/>)
/// — sem ele a saída é RECUSADA antes de o romaneio nascer, o que derrubaria
/// <see cref="CompletedShipmentTicket_CreatesShipmentCarryingTheLot"/> por um motivo alheio ao que
/// ele prova. <see cref="SeedTransshipmentLotAsync"/> passou a montar também a carga/transbordo
/// aberto do MESMO caminhão quando há saldo de abertura — a regra de negócio em si (o vínculo, a
/// liberação) é coberta à parte por <c>WeighingTicketTransshipmentLotExitTriggerTests</c>.
/// </remarks>
public class WeighingTicketTransshipmentLotTests
{
    private const string WarehouseCode = "ARM01";
    private const string OriginWarehouse = "ARM02";
    private const string LotCode = "L-TRANSSHIP-01";
    private const string CardCode = "C0001";
    private const string ItemCode = "SOJA";
    private const string TruckCode = "ABC1D23";

    private static WeighingTicketsCompletedService Service(IUnitOfWork db) => new(
        db,
        new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
        new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
        new StorageTransactionsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém Teste" }),
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
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém Teste" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)),
        new FakeStringLocalizer<Resource>(),
        NullLogger<WeighingTicketsCompletedService>.Instance);

    /// <summary>
    /// Grava o lote de transbordo em armazém próprio. <paramref name="openingBalance"/> só é usado
    /// pelo cenário de SAÍDA: sem saldo prévio no lote, a Validate() do próprio serviço recusaria o
    /// embarque antes de chegar ao ponto que este teste quer provar.
    /// </summary>
    /// <remarks>
    /// ⚠️ Redesenho do GAC-1181 fase 2: quando há saldo de abertura, este helper também monta a
    /// carga/transbordo ABERTO do MESMO caminhão (<see cref="TruckCode"/>) — sem isso, a saída
    /// (<see cref="CompletedShipmentTicket_CreatesShipmentCarryingTheLot"/>) seria RECUSADA pelo
    /// gatilho novo (<see cref="ShipmentLoadTransshipmentRules.ResolveOpenTransshipmentForLotExitAsync"/>)
    /// antes de o romaneio nascer, por falta de transbordo — e não pelo motivo que o teste do lote
    /// preservado quer provar.
    /// </remarks>
    private static async Task<IUnitOfWork> SeedTransshipmentLotAsync(decimal openingBalance = 0)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = WarehouseCode,
            IsOwn = true,
        });

        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode,
            Description = "Lote de transbordo",
            CardCode = CardCode,
            ItemCode = ItemCode,
            WarehouseCode = WarehouseCode,
            UoM = "KG",
            Nature = StorageAddressNature.Transshipment,
        });

        if (openingBalance > 0)
        {
            var entryReceipt = new StorageTransaction
            {
                Key = Guid.NewGuid(),
                StorageAddressCode = LotCode,
                TransactionType = StorageTransactionType.Receipt,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                NetWeight = openingBalance,
                GrossWeight = openingBalance,
                CardCode = CardCode,
                ItemCode = ItemCode,
                UnitOfMeasureCode = "KG",
                WarehouseCode = WarehouseCode,
            };
            db.Context.StorageTransactions.Add(entryReceipt);

            var load = new ShipmentLoad
            {
                Key = Guid.NewGuid(),
                Code = "CG-TRANSSHIP-01",
                ItemCode = ItemCode,
                ItemName = "Soja em grãos",
                UnitOfMeasureCode = "KG",
                TruckCode = TruckCode,
                WarehouseCode = OriginWarehouse,
                Status = ShipmentLoadStatus.InTransshipment,
                TotalQuantity = openingBalance,
            };

            var origin = new StorageTransaction
            {
                Key = Guid.NewGuid(),
                CardCode = CardCode,
                ItemCode = ItemCode,
                UnitOfMeasureCode = "KG",
                WarehouseCode = OriginWarehouse,
                TruckCode = TruckCode,
                GrossWeight = openingBalance,
                NetWeight = openingBalance,
                TransactionType = StorageTransactionType.SalesShipment,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                ShipmentLoadKey = load.Key,
            };

            var transshipment = new ShipmentLoadTransshipment
            {
                ShipmentLoadKey = load.Key,
                Sequence = 1,
                WarehouseCode = WarehouseCode,
                WarehouseName = "Armazém Teste",
                OutgoingQuantity = openingBalance,
                EntryQuantity = openingBalance,
            };

            db.Context.ShipmentLoads.Add(load);
            db.Context.StorageTransactions.Add(origin);
            db.Context.ShipmentLoadsTransshipments.Add(transshipment);
            await db.SaveChangesAsync();

            entryReceipt.ShipmentLoadTransshipmentKey = transshipment.Key;
            transshipment.EntryStorageTransactionKey = entryReceipt.Key;
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static WeighingTicket NewTicket(WeighingTicketType type) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        ItemCode = ItemCode,
        CardCode = CardCode,
        TruckCode = TruckCode,
        TruckDriverCode = "1",
        Stage = WeighingTicketStage.ReadyForCompleting,
        StorageAddressCode = LotCode,
        FirstWeighValue = 50000,
        SecondWeighValue = 20000,
    };

    [Fact]
    public async Task CompletedReceiptTicket_CreatesReceiptCarryingTheLot()
    {
        var db = await SeedTransshipmentLotAsync();

        var ticket = NewTicket(WeighingTicketType.Receipt);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Service(db).ExecuteAsync(ticket.Key, "tester");

        var transaction = await db.Context.StorageTransactions
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(StorageTransactionType.Receipt, transaction.TransactionType);
        Assert.Equal(LotCode, transaction.StorageAddressCode);
        Assert.Equal(StorageTransactionsStatus.Confirmed, transaction.TransactionStatus);
    }

    [Fact]
    public async Task CompletedShipmentTicket_CreatesShipmentCarryingTheLot()
    {
        // Saldo de abertura (40000) maior que o embarque do ticket (30000): sem isso a própria
        // Validate() do serviço recusaria o embarque antes de gerar o romaneio.
        var db = await SeedTransshipmentLotAsync(openingBalance: 40000);

        var ticket = NewTicket(WeighingTicketType.Shipment);
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Service(db).ExecuteAsync(ticket.Key, "tester");

        var transaction = await db.Context.StorageTransactions
            .SingleAsync(x => x.WeighingTicketKey == ticket.Key);

        Assert.Equal(StorageTransactionType.Shipment, transaction.TransactionType);
        Assert.Equal(LotCode, transaction.StorageAddressCode);
        Assert.Equal(StorageTransactionsStatus.Confirmed, transaction.TransactionStatus);
    }
}
