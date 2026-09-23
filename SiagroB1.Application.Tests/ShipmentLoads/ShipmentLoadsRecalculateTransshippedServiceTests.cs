using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// <see cref="ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync"/> com dados
/// reais de transbordo (GAC-1181) — a Task 3 só pôde testar a fórmula por unidade, porque na
/// época não existia nenhum serviço de escrita que criasse um transbordo de verdade.
/// </summary>
/// <remarks>
/// A checagem é load-bearing (ver <see cref="ShipmentLoadTransshipmentRules.EnsureIsLastAsync"/>):
/// ela só enxerga o ÚLTIMO transbordo por <see cref="ShipmentLoadTransshipment.Sequence"/>, então
/// os quatro testes cobrem exatamente isso — o que fecha, o que não fecha, e que um transbordo
/// mais antigo aberto não conta se o último já foi fechado.
/// </remarks>
public class ShipmentLoadsRecalculateTransshippedServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoad Load(decimal totalQuantity = 60_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = totalQuantity,
            Status = ShipmentLoadStatus.Open,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private ShipmentLoadTransshipment Transshipment(Guid loadKey, int sequence, decimal outgoing = 30_000)
    {
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = loadKey,
            Sequence = sequence,
            WarehouseCode = "ARM99",
            OutgoingQuantity = outgoing,
        };
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        return transshipment;
    }

    /// <summary>O romaneio de SAÍDA (reembarque) que fecha um transbordo.</summary>
    private void LinkedOutgoingShipment(
        Guid loadKey,
        Guid transshipmentKey,
        decimal grossWeight = 30_000,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R-RECARGA",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = status,
            ShipmentLoadKey = loadKey,
            ShipmentLoadTransshipmentKey = transshipmentKey,
        });
    }

    [Fact]
    public async Task The_last_transshipment_without_a_linked_shipment_is_open()
    {
        var load = Load();
        Transshipment(load.Key, sequence: 1);
        await _db.Context.SaveChangesAsync();

        var hasOpen = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(_db.Context, load.Key);

        Assert.True(hasOpen);
    }

    [Fact]
    public async Task A_confirmed_linked_outgoing_shipment_closes_the_transshipment()
    {
        var load = Load();
        var transshipment = Transshipment(load.Key, sequence: 1);
        LinkedOutgoingShipment(load.Key, transshipment.Key!.Value);
        await _db.Context.SaveChangesAsync();

        var hasOpen = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(_db.Context, load.Key);

        Assert.False(hasOpen);
    }

    /// <summary>
    /// Um embarque CANCELADO não conta como saída — o transbordo continua aberto, senão a
    /// mercadoria desapareceria do saldo sem ter saído de fato do armazém intermediário.
    /// </summary>
    [Fact]
    public async Task A_cancelled_linked_shipment_does_not_close_the_transshipment()
    {
        var load = Load();
        var transshipment = Transshipment(load.Key, sequence: 1);
        LinkedOutgoingShipment(
            load.Key, transshipment.Key!.Value, status: StorageTransactionsStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        var hasOpen = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(_db.Context, load.Key);

        Assert.True(hasOpen);
    }

    /// <summary>
    /// Só o ÚLTIMO por <c>Sequence</c> importa: um transbordo mais antigo deixado aberto não
    /// pesa se o mais recente já foi fechado — é essa seletividade que faz a trava de "não
    /// empilhar" ser exatamente sobre o mais recente, nunca sobre qualquer um.
    /// </summary>
    [Fact]
    public async Task Only_the_last_sequence_is_considered()
    {
        var load = Load();
        Transshipment(load.Key, sequence: 1); // deixado aberto de propósito
        var last = Transshipment(load.Key, sequence: 2);
        LinkedOutgoingShipment(load.Key, last.Key!.Value);
        await _db.Context.SaveChangesAsync();

        var hasOpen = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(_db.Context, load.Key);

        Assert.False(hasOpen);
    }
}
