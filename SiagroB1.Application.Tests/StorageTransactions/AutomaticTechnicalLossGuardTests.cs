using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// GAC-18: a quebra técnica automática pertence ao cálculo diário. Cancelá-la ou estorná-la à mão
/// deixaria a cobrança da quebra viva, e o cálculo não a recriaria. A recusa precisa dizer o
/// caminho certo (Reprocessar Saldo por Lote), e não a mensagem técnica genérica em inglês.
/// </summary>
public class AutomaticTechnicalLossGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<StorageTransaction> SeedLossAsync()
    {
        var loss = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "00012853",
            StorageAddressCode = "00000042",
            TransactionType = StorageTransactionType.TechnicalLoss,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionDate = new DateTime(2026, 3, 16),
            NetWeight = 1.021m,
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            Comments = "QUEBRA_TECNICA_20260316_20260316",
        };
        _db.Context.StorageTransactions.Add(loss);
        await _db.SaveChangesAsync();
        return loss;
    }

    [Fact]
    public async Task Cancel_points_to_the_lot_reprocessing()
    {
        var loss = await SeedLossAsync();
        var service = new StorageTransactionsCancelService(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(loss.Key, "tester"));

        Assert.Contains("00012853", ex.Message);
        Assert.Contains("Reprocessar Saldo por Lote", ex.Message);
    }

    [Fact]
    public async Task Reverse_points_to_the_lot_reprocessing()
    {
        var loss = await SeedLossAsync();
        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(loss.Key, "tester"));

        Assert.Contains("Reprocessar Saldo por Lote", ex.Message);
    }
}
