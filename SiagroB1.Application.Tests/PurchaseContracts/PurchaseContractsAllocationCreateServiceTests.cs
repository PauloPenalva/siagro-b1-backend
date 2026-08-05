using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsAllocationCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>
    /// A safra era OBRIGATÓRIA aqui, e o motivo virou correção de produção: o serviço
    /// carregava o contrato por <c>PurchaseContractsGetService.GetByIdAsync</c>, que
    /// inclui <c>HarvestSeason</c>. Como <see cref="PurchaseContract.HarvestSeasonCode"/>
    /// é FK obrigatória, o EF traduz o Include como INNER JOIN: sem a linha em
    /// HARVEST_SEASONS o contrato sumia da consulta e o serviço recebia null — o que
    /// silenciosamente pulava a guarda de contrato encerrado (<c>?.</c>) e a atribuição
    /// de AllocatedVolume, sem erro nenhum, deixando o saldo físico defasado.
    ///
    /// O serviço passou a carregar o contrato sem includes, então a semente não é mais
    /// necessária — fica porque o cenário com safra é o realista.
    /// </summary>
    public PurchaseContractsAllocationCreateServiceTests()
    {
        _db.Context.HarvestSeasons.Add(new HarvestSeason { Code = "24/25", Name = "Safra 24/25" });
        _db.Context.SaveChanges();
    }

    private PurchaseContractsAllocationCreateService CreateService() => new(
        _db,
        new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));

    private static StorageTransaction NewPurchase(decimal netWeight, decimal available) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TruckCode = "TRK01",
        ProcessingCostCode = "PC01",
        TransactionType = StorageTransactionType.Purchase,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        NetWeight = netWeight,
        AvaiableVolumeToAllocate = available,
    };

    private static PurchaseContract NewContract(decimal totalVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
    };

    private async Task<StorageTransaction> ReloadStAsync(Guid key) =>
        await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private async Task<PurchaseContract> ReloadContractAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task ExecuteAsync_CreatesAllocation_AndDerivesAvailableFromNetWeight()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");

        Assert.Equal(1, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
        Assert.Equal(700m, (await ReloadStAsync(st.Key)).AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task ExecuteAsync_SecondAllocation_DerivesFromSum_NotIncrementalDrift()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");
        await CreateService().ExecuteAsync(pc.Key, st.Key, 200m, "tester");

        // NetWeight 1000 − Σ(300 + 200) = 500
        Assert.Equal(500m, (await ReloadStAsync(st.Key)).AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task ExecuteAsync_SelfHealsAvailableVolumeInflatedByDrift()
    {
        // Saldo persistido inflado por drift antigo (9999). Passa a validação
        // (300 < 9999) e, após criar, é derivado corretamente: NetWeight − Σ.
        // Obs.: um saldo corrompido PARA BAIXO ainda bloqueia o create, pois a
        // validação de volume lê o campo — no create ele segue autoritativo.
        var st = NewPurchase(netWeight: 1000m, available: 9999m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");

        Assert.Equal(700m, (await ReloadStAsync(st.Key)).AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task ExecuteAsync_SetsContractAllocatedVolume_FromSignedSum()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");

        var contract = await ReloadContractAsync(pc.Key);
        Assert.Equal(300m, contract.AllocatedVolume);
        Assert.Equal(4700m, contract.AvaiableVolume); // 5000 − 300
    }

    [Fact]
    public async Task ExecuteAsync_TwoAllocations_AccumulateContractAllocatedVolume()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");
        await CreateService().ExecuteAsync(pc.Key, st.Key, 200m, "tester");

        var contract = await ReloadContractAsync(pc.Key);
        Assert.Equal(500m, contract.AllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_ContractFinished_ThrowsAndDoesNotAllocate()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        pc.Status = ContractStatus.Finished;
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester"));

        Assert.Equal(0, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
    }
}
