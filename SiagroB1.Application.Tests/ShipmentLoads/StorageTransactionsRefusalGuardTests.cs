using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// A devolução gerada pela RECUSA de uma carga não pode ser cancelada nem estornada pela tela de
/// Romaneios.
/// </summary>
/// <remarks>
/// O guard existente barra por <c>ShipmentLoadKey</c>, e a devolução tem esse campo NULO de
/// propósito (com ele, ela inflaria o volume embarcado da carga). Sem uma segunda condição sobre
/// <c>RefusedFromShipmentLoadKey</c>, ela escapa: cancelar por lá derrubaria em silêncio o
/// <c>ReturnedToWarehouseQuantity</c> da carga, reabrindo-a para faturamento com a mercadoria já
/// creditada em outro armazém.
/// </remarks>
public class StorageTransactionsRefusalGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsCancelService Cancel() =>
        new(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

    /// <summary>
    /// <c>balanceService</c> vai nulo de propósito: ele só é usado em <c>ValidateBalance</c>, que
    /// roda DEPOIS do guard sob teste. Se algum dia o guard for movido para baixo, este null
    /// vira NRE — e é justamente o alarme que se quer.
    /// </summary>
    private StorageTransactionsReverseService Reverse() =>
        new(_db,
            null!,
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

    private async Task<StorageTransaction> SeedRefusalReturnAsync()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000021",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            BranchCode = "01",
            TotalQuantity = 40_000m,
            ReturnedToWarehouseQuantity = 40_000m,
            Status = ShipmentLoadStatus.Returned,
        };

        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000777",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.ShipmentLoad,
            // Nulo DE PROPÓSITO — é o que faz o guard antigo não pegar.
            ShipmentLoadKey = null,
            RefusedFromShipmentLoadKey = load.Key,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.StorageTransactions.Add(entry);
        await _db.SaveChangesAsync();

        return entry;
    }

    [Fact]
    public async Task A_refusal_return_cannot_be_cancelled_from_the_shipments_screen()
    {
        var entry = await SeedRefusalReturnAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Cancel().ExecuteAsync(entry.Key, "tester", TransactionCode.ShipmentLoad));

        Assert.Contains("devolução da recusa da carga", error.Message);
        Assert.Contains("CG000021", error.Message);
    }

    [Fact]
    public async Task A_refusal_return_cannot_be_reversed_from_the_shipments_screen()
    {
        var entry = await SeedRefusalReturnAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Reverse().ExecuteAsync(entry.Key, "tester", TransactionCode.ShipmentLoad));

        Assert.Contains("devolução da recusa da carga", error.Message);
        Assert.Contains("CG000021", error.Message);
    }
}
