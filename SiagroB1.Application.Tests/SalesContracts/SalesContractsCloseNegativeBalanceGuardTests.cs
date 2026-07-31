using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Contrato faturado ALÉM do volume contratado não pode ser encerrado: encerrar esconde o
/// erro de distribuição (o contrato some das listas de alocação e do recálculo em lote) e
/// deixa o volume excedente órfão. O guard decide sobre o saldo RECALCULADO do ledger —
/// AllocatedVolume é persistido-derivado e pode estar defasado.
/// </summary>
public class SalesContractsCloseNegativeBalanceGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsCloseService CloseService() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context),
            TestNotificationOutbox.For(_db.Context));

    private static SalesContract NewContract(decimal totalVolume, decimal allocatedVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-NEG",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Type = ContractType.Fixed,
        Status = ContractStatus.Approved,
    };

    /// <param name="ledgerVolume">
    /// Volume gravado no ledger. Quando difere de <paramref name="allocatedVolume"/>,
    /// reproduz o drift do agregado persistido.
    /// </param>
    private async Task<SalesContract> SeedAsync(
        decimal totalVolume, decimal allocatedVolume, decimal? ledgerVolume = null)
    {
        var contract = NewContract(totalVolume, allocatedVolume);
        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            SalesInvoiceItemKey = item.Key!.Value,
            Volume = ledgerVolume ?? allocatedVolume,
            Origin = SalesContractAllocationOrigin.Billing,
        });
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private async Task<SalesContract> ReloadAsync(Guid key) =>
        await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Close_NegativeBalance_Throws_AndKeepsApproved()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1200m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(sc.Key, "tester"));

        Assert.Contains("além do volume contratado", ex.Message);
        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_PositiveBalance_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 600m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_ZeroBalance_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1000m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    /// <summary>
    /// O motivo de o guard recalcular: agregado persistido negativo por drift, ledger dizendo
    /// que o contrato está são. Ler o persistido bloquearia um contrato correto.
    /// </summary>
    [Fact]
    public async Task Close_PersistedBalanceNegativeButLedgerHealthy_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1200m, ledgerVolume: 800m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }
}
