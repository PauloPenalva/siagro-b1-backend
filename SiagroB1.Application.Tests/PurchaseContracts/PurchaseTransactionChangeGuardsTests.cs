using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Proteções depois da troca de fornecedor/armazém: os romaneios da troca (original, estorno 9 e
/// novo 8) não são cancelados/estornados por aqui, e a alocação da tela recusa contrato de outro
/// fornecedor/armazém (quem troca é a ação própria).
/// </summary>
public class PurchaseTransactionChangeGuardsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsCancelService CancelService() =>
        new(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

    private StorageTransactionsReverseService ReverseService() =>
        new(_db, new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context), new FakeStringLocalizer<Resource>());

    private PurchaseContractsAllocationCreateService AllocationService() =>
        new(_db, new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));

    private async Task<StorageTransaction> SeedTransactionAsync(
        string code,
        StorageTransactionType type = StorageTransactionType.Purchase,
        string cardCode = "F0001",
        string warehouseCode = "01",
        Guid? changeKey = null)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = cardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouseCode,
            TruckCode = "TRK01",
            ProcessingCostCode = "PC01",
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 1_000m,
            AvaiableVolumeToAllocate = 1_000m,
            PurchaseTransactionChangeKey = changeKey,
        };
        _db.Context.StorageTransactions.Add(st);
        await _db.Context.SaveChangesAsync();
        return st;
    }

    private async Task<PurchaseContract> SeedContractAsync(string cardCode = "F0001", string warehouseCode = "01")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = cardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouseCode,
            Status = ContractStatus.Approved,
            TotalVolume = 10_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private async Task<PurchaseTransactionChange> SeedChangeAsync(StorageTransaction original)
    {
        var change = new PurchaseTransactionChange
        {
            Key = Guid.NewGuid(),
            OriginalStorageTransactionKey = original.Key,
            ReturnStorageTransactionKey = Guid.NewGuid(),
            NewStorageTransactionKey = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            Quantity = 1_000m,
            OriginalCardCode = original.CardCode,
            OriginalWarehouseCode = original.WarehouseCode,
            NewCardCode = "F0002",
            NewWarehouseCode = "02",
            Reason = "motivo",
        };
        _db.Context.PurchaseTransactionChanges.Add(change);
        await _db.Context.SaveChangesAsync();
        return change;
    }

    [Fact]
    public async Task Cancel_RefusesChangeReturn()
    {
        var ret = await SeedTransactionAsync("RC-9", StorageTransactionType.PurchaseReturn, changeKey: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => CancelService().ExecuteAsync(ret.Key, "tester"));

        Assert.Equal(
            "O romaneio RC-9 faz parte de uma troca de fornecedor/armazém e não pode ser cancelado por aqui.",
            ex.Message);
    }

    [Fact]
    public async Task Cancel_RefusesReplacedOriginal()
    {
        var original = await SeedTransactionAsync("RC-1");
        await SeedChangeAsync(original);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => CancelService().ExecuteAsync(original.Key, "tester"));

        Assert.Equal(
            "O romaneio RC-1 faz parte de uma troca de fornecedor/armazém e não pode ser cancelado por aqui.",
            ex.Message);
    }

    [Fact]
    public async Task Reverse_RefusesNewTransaction()
    {
        var neu = await SeedTransactionAsync("RC-2", changeKey: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => ReverseService().ExecuteAsync(neu.Key, "tester"));

        Assert.Equal(
            "O romaneio RC-2 faz parte de uma troca de fornecedor/armazém e não pode voltar a pendente por aqui.",
            ex.Message);
    }

    [Fact]
    public async Task PublicAllocation_RefusesOtherSupplier()
    {
        var st = await SeedTransactionAsync("RC-3");
        var contract = await SeedContractAsync(cardCode: "F0002");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester"));

        Assert.Equal("O contrato tem outro fornecedor ou armazém: use a troca de fornecedor/armazém.", ex.Message);
        Assert.False(await _db.Context.PurchaseContractsAllocations.AnyAsync());
    }

    [Fact]
    public async Task PublicAllocation_RefusesOtherWarehouse()
    {
        var st = await SeedTransactionAsync("RC-4");
        var contract = await SeedContractAsync(warehouseCode: "02");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester"));

        Assert.Equal("O contrato tem outro fornecedor ou armazém: use a troca de fornecedor/armazém.", ex.Message);
    }

    [Fact]
    public async Task PublicAllocation_SameSupplierAndWarehouse_StillWorks()
    {
        var st = await SeedTransactionAsync("RC-5", cardCode: "f0001", warehouseCode: "a1");
        var contract = await SeedContractAsync(cardCode: "F0001", warehouseCode: "A1");

        await AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester");

        var alloc = await _db.Context.PurchaseContractsAllocations.SingleAsync();
        Assert.Equal(500m, alloc.Volume);
    }

    [Fact]
    public async Task PublicAllocation_ShippingLegWithOtherWarehouse_NotBlockedByDivergence()
    {
        // Pernada de compra de uma Expedição: o armazém é o da liberação, que pode diferir do
        // Local de Entrega do contrato (decisão 7 do spec).
        var st = await SeedTransactionAsync("RC-6", warehouseCode: "09");
        var contract = await SeedContractAsync(warehouseCode: "01");
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "09",
            ReleasedQuantity = 1_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(release);
        st.ShipmentReleaseKey = release.Key;
        _db.Context.ShippingTransactions.Add(new ShippingTransaction
        {
            Key = Guid.NewGuid(),
            PurchaseStorageTransactionKey = st.Key,
        });
        await _db.Context.SaveChangesAsync();

        await AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester");

        Assert.True(await _db.Context.PurchaseContractsAllocations.AnyAsync());
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseReturn)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement)]
    [InlineData(StorageTransactionType.PurchasePriceComplement)]
    public async Task PublicAllocation_NonPurchaseType_OtherSupplier_RefusedWithoutChangeHint(StorageTransactionType type)
    {
        // Devolução e complementos não têm troca de fornecedor/armazém: vale a regra de antes
        // (mesmo fornecedor), sem mandar o usuário para uma troca que os recusaria.
        var st = await SeedTransactionAsync("RC-7", type);
        var contract = await SeedContractAsync(cardCode: "F0002");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester"));

        Assert.Equal("O contrato é de outro fornecedor.", ex.Message);
        Assert.False(await _db.Context.PurchaseContractsAllocations.AnyAsync());
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseReturn)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement)]
    [InlineData(StorageTransactionType.PurchasePriceComplement)]
    public async Task PublicAllocation_NonPurchaseType_OtherWarehouse_AllocatesNormally(StorageTransactionType type)
    {
        var st = await SeedTransactionAsync("RC-8", type, cardCode: "f0001", warehouseCode: "01");
        var contract = await SeedContractAsync(cardCode: "F0001", warehouseCode: "02");

        await AllocationService().ExecuteWithTransactionAsync(contract.Key, st.Key, 500m, "tester");

        Assert.True(await _db.Context.PurchaseContractsAllocations.AnyAsync(a => a.StorageTransactionKey == st.Key));
    }
}
