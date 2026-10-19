using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Troca de fornecedor/armazém na alocação de romaneios de compra: alocar um romaneio 8 em
/// contrato de outro fornecedor/armazém gera um estorno 9 na origem e um 8 novo, confirmado, no
/// fornecedor/armazém do contrato, alocado e amarrado por um <see cref="PurchaseTransactionChange"/>.
/// </summary>
public class PurchaseTransactionsChangeSupplierServiceTests
{
    private static readonly DateTime TransactionDate = new(2026, 9, 5);
    private const string TransactionTime = "10:30:00";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsGetService GetService() =>
        new(_db, NullLogger<StorageTransactionsGetService>.Instance);

    private PurchaseTransactionsChangeSupplierService Service() =>
        new(_db,
            new StorageTransactionsCreateService(
                _db,
                _docNumbers,
                new FakeBusinessPartnerService(new() { ["F0001"] = "Produtor Um", ["F0002"] = "Produtor Dois" }),
                new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
                new FakeWarehouseService(new() { ["01"] = "Armazém 01", ["02"] = "Armazém 02" }),
                Recalc(),
                new ShipmentReleaseMovementGuardService(_db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new StorageTransactionsConfirmedService(
                _db, new FakeStringLocalizer<Resource>(), Recalc(),
                new ShipmentReleaseMovementGuardService(_db.Context),
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            new ShippingReleaseChangeReconciliationGuard(_db.Context));

    private async Task<PurchaseContract> SeedContractAsync(
        string code,
        string cardCode = "F0002",
        string warehouseCode = "02",
        decimal totalVolume = 100_000m,
        string itemCode = "SOJA",
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = cardCode,
            CardName = cardCode == "F0001" ? "Produtor Um" : "Produtor Dois",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouseCode,
            DeliveryLocationName = $"Armazém {warehouseCode}",
            Status = status,
            TotalVolume = totalVolume,
            AllocatedVolume = 0m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private async Task<StorageTransaction> SeedOriginalAsync(
        decimal gross = 30_000m,
        decimal drying = 0m,
        decimal cleaning = 0m,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        string? invoiceNumber = null,
        string? chaveNFe = null,
        TransactionCode? origin = TransactionCode.StorageTransaction)
    {
        var net = gross - drying - cleaning;
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RC-0001",
            CardCode = "F0001",
            CardName = "Produtor Um",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            WarehouseName = "Armazém 01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = status,
            TransactionDate = TransactionDate,
            TransactionTime = TransactionTime,
            TruckCode = "ABC1D23",
            GrossWeight = gross,
            DryingDiscount = drying,
            CleaningDiscount = cleaning,
            NetWeight = net,
            AvaiableVolumeToAllocate = net,
            DryingServicePrice = 120m,
            CleaningServicePrice = 80m,
            ReceiptServicePrice = 50m,
            InvoiceNumber = invoiceNumber,
            InvoiceSerie = invoiceNumber == null ? null : "1",
            InvoiceQty = invoiceNumber == null ? 0m : net,
            ChaveNFe = chaveNFe,
            TransactionOrigin = origin,
        };
        _db.Context.StorageTransactions.Add(st);
        await _db.Context.SaveChangesAsync();
        return st;
    }

    /// <summary>Alocação já existente do original num contrato do próprio fornecedor (troca parcial).</summary>
    private async Task<PurchaseContractAllocation> SeedAllocationAsync(StorageTransaction st, PurchaseContract contract, decimal volume)
    {
        var alloc = new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = st.Key,
            Volume = volume,
        };
        _db.Context.PurchaseContractsAllocations.Add(alloc);
        contract.AllocatedVolume += volume;
        st.AvaiableVolumeToAllocate -= volume;
        await _db.Context.SaveChangesAsync();
        return alloc;
    }

    private async Task<PurchaseFiscalReconciliation> SeedReconciliationLinkAsync(StorageTransaction st)
    {
        var link = new PurchaseFiscalReconciliation
        {
            Key = Guid.NewGuid(),
            PurchaseInvoiceItemKey = Guid.NewGuid(),
            StorageTransactionKey = st.Key,
            Quantity = st.NetWeight,
        };
        _db.Context.PurchaseFiscalReconciliations.Add(link);
        await _db.Context.SaveChangesAsync();
        return link;
    }

    private Task<StorageTransaction> LoadAsync(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<PurchaseContract> LoadContractAsync(Guid key) =>
        _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    // ─────────────────────────────── troca total ───────────────────────────────

    [Fact]
    public async Task FullChange_CreatesReturnAndNewTransaction()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");

        var result = await Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "  Fornecedor errado  ", "tester");

        var change = Assert.Single(await _db.Context.PurchaseTransactionChanges.AsNoTracking().ToListAsync());
        Assert.Equal(original.Key, change.OriginalStorageTransactionKey);
        Assert.Equal(contract.Key, change.PurchaseContractKey);
        Assert.Equal(30_000m, change.Quantity);
        Assert.Equal(30_000m, change.AllocatedVolume);
        Assert.Equal("Fornecedor errado", change.Reason);
        Assert.Equal(("F0001", "01", "F0002", "02"),
            (change.OriginalCardCode, change.OriginalWarehouseCode, change.NewCardCode, change.NewWarehouseCode));

        var ret = await LoadAsync(change.ReturnStorageTransactionKey);
        Assert.Equal(StorageTransactionType.PurchaseReturn, ret.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, ret.TransactionStatus);
        Assert.Equal(("F0001", "01"), (ret.CardCode, ret.WarehouseCode));
        Assert.Equal(30_000m, ret.GrossWeight);
        Assert.Equal(30_000m, ret.NetWeight);
        Assert.Equal(0m, ret.AvaiableVolumeToAllocate);
        Assert.Equal(TransactionDate, ret.TransactionDate);
        Assert.Equal(TransactionTime, ret.TransactionTime);
        Assert.Equal(change.Key, ret.PurchaseTransactionChangeKey);
        Assert.Equal((0m, 0m, 0m, 0m),
            (ret.DryingServicePrice, ret.CleaningServicePrice, ret.ReceiptServicePrice, ret.ShipmentPrice));
        Assert.Equal(result.ReturnCode, ret.Code);

        var neu = await LoadAsync(change.NewStorageTransactionKey);
        Assert.Equal(StorageTransactionType.Purchase, neu.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, neu.TransactionStatus);
        Assert.Equal(("F0002", "Produtor Dois", "02", "Armazém 02"),
            (neu.CardCode, neu.CardName, neu.WarehouseCode, neu.WarehouseName));
        Assert.Equal(30_000m, neu.GrossWeight);
        Assert.Equal(30_000m, neu.NetWeight);
        Assert.Equal(0m, neu.AvaiableVolumeToAllocate);
        Assert.Equal(TransactionDate, neu.TransactionDate);
        Assert.Equal(TransactionTime, neu.TransactionTime);
        Assert.Equal("ABC1D23", neu.TruckCode);
        Assert.Equal(change.Key, neu.PurchaseTransactionChangeKey);
        Assert.Equal((0m, 0m, 0m, 0m),
            (neu.DryingServicePrice, neu.CleaningServicePrice, neu.ReceiptServicePrice, neu.ShipmentPrice));
        Assert.Equal(result.NewCode, neu.Code);

        // Estorno sem alocação; romaneio novo alocado no contrato de destino.
        Assert.False(await _db.Context.PurchaseContractsAllocations.AnyAsync(x => x.StorageTransactionKey == ret.Key));
        var alloc = Assert.Single(await _db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == neu.Key).ToListAsync());
        Assert.Equal((contract.Key, 30_000m), (alloc.PurchaseContractKey, alloc.Volume));
        Assert.Equal(30_000m, (await LoadContractAsync(contract.Key)).AllocatedVolume);

        // Original intacto, sem saldo alocável e sem a marca (só estorno e novo a carregam).
        var orig = await LoadAsync(original.Key);
        Assert.Equal(("F0001", "01", 30_000m), (orig.CardCode, orig.WarehouseCode, orig.NetWeight));
        Assert.Equal(0m, orig.AvaiableVolumeToAllocate);
        Assert.Null(orig.PurchaseTransactionChangeKey);
        Assert.Equal(120m, orig.DryingServicePrice);
    }

    [Fact]
    public async Task FullChange_MovesInvoiceAndReconciliationLinks()
    {
        const string chave = "41260900000000000000550010000001231000001230";
        var original = await SeedOriginalAsync(invoiceNumber: "123", chaveNFe: chave);
        original.FiscalReconciledAt = DateTime.Now;
        original.FiscalReconciledBy = "conferente";
        await _db.Context.SaveChangesAsync();
        var link = await SeedReconciliationLinkAsync(original);
        var contract = await SeedContractAsync("PC-002");

        await Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "Fornecedor errado", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        var neu = await LoadAsync(change.NewStorageTransactionKey);
        Assert.Equal(("123", "1", 30_000m, chave), (neu.InvoiceNumber, neu.InvoiceSerie, neu.InvoiceQty, neu.ChaveNFe));

        var orig = await LoadAsync(original.Key);
        Assert.Null(orig.InvoiceNumber);
        Assert.Null(orig.InvoiceSerie);
        Assert.Equal(0m, orig.InvoiceQty);
        Assert.Null(orig.ChaveNFe);
        // Vínculo mudou: a confirmação manual da conciliação do original é limpa.
        Assert.Null(orig.FiscalReconciledAt);
        Assert.Null(orig.FiscalReconciledBy);

        var ret = await LoadAsync(change.ReturnStorageTransactionKey);
        Assert.Null(ret.InvoiceNumber);
        Assert.Null(ret.ChaveNFe);

        var moved = await _db.Context.PurchaseFiscalReconciliations.AsNoTracking().SingleAsync(x => x.Key == link.Key);
        Assert.Equal(neu.Key, moved.StorageTransactionKey);
    }

    [Fact]
    public async Task OnlyWarehouseDiffers_IsAChange()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002", cardCode: "F0001", warehouseCode: "02");

        await Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "Armazém errado", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        var neu = await LoadAsync(change.NewStorageTransactionKey);
        Assert.Equal(("F0001", "02"), (neu.CardCode, neu.WarehouseCode));
    }

    [Fact]
    public async Task LegacyOriginalWithoutRoyalty_IsChangedOnControlledItem()
    {
        // Produto passou a controlar royalties depois do lançamento: o romaneio novo é do sistema,
        // o usuário não tem como preencher a situação.
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        await _db.Context.SaveChangesAsync();
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");

        await Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        var neu = await LoadAsync(change.NewStorageTransactionKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, neu.TransactionStatus);
        Assert.Null(neu.RoyaltySituation);
    }

    // ─────────────────────────────── travas ───────────────────────────────

    [Fact]
    public async Task SameSupplierAndWarehouse_IsRefused()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-001", cardCode: "F0001", warehouseCode: "01");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal("Fornecedor e armazém são os mesmos do contrato: use a alocação normal.", ex.Message);
    }

    [Fact]
    public async Task OtherItem_IsRefused()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002", itemCode: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal("O contrato é de outro produto.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EmptyReason_IsRefused(string? reason)
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, reason, "tester"));

        Assert.Equal("Informe o motivo da troca de fornecedor/armazém.", ex.Message);
    }

    [Fact]
    public async Task PendingTransaction_IsRefused()
    {
        var original = await SeedOriginalAsync(status: StorageTransactionsStatus.Pending);
        var contract = await SeedContractAsync("PC-002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal(
            "Só romaneio de compra confirmado com saldo a alocar pode ter o fornecedor/armazém trocado.",
            ex.Message);
    }

    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Draft)]
    public async Task ContractNotApproved_IsRefused(ContractStatus status)
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002", status: status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal("Contrato encerrado ou não aprovado: não é possível alocar.", ex.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegOfAnotherFlow_IsRefused(bool shipping)
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");
        if (shipping)
            _db.Context.ShippingTransactions.Add(new ShippingTransaction
            {
                PurchaseStorageTransactionKey = original.Key,
                SalesStorageTransactionKey = Guid.NewGuid(),
            });
        else
            _db.Context.StorageEntryTransactions.Add(new StorageEntryTransaction
            {
                PurchaseStorageTransactionKey = original.Key,
                ReceiptStorageTransactionKey = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
            });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal(
            "O romaneio RC-0001 faz parte de outro fluxo (expedição ou entrada em armazenagem) e não pode ter " +
            "o fornecedor/armazém trocado por aqui.",
            ex.Message);
    }

    [Theory]
    [InlineData(TransactionCode.OwnershipTransfer)]
    [InlineData(TransactionCode.ShipmentLoad)]
    [InlineData(TransactionCode.WarehouseReconciliation)]
    [InlineData(null)]
    public async Task NonStandaloneOrigin_IsRefused(TransactionCode? origin)
    {
        // Só o romaneio lançado na tela de romaneios (origem StorageTransaction) é trocado aqui:
        // os gerados por outro fluxo têm o próprio estorno.
        var original = await SeedOriginalAsync(origin: origin);
        var contract = await SeedContractAsync("PC-002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal(
            "O romaneio RC-0001 foi gerado por outro fluxo e não pode ter o fornecedor/armazém trocado por aqui.",
            ex.Message);
        Assert.Single(await _db.Context.StorageTransactions.ToListAsync());
    }

    [Theory]
    [InlineData(0, "Volume de alocação inválido.")]
    [InlineData(30_001, "O volume informado é maior que o saldo disponível do romaneio.")]
    public async Task InvalidVolume_IsRefused(int volume, string message)
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, volume, "motivo", "tester"));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task ApprovedReconciliationAfterDate_IsRefused()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");
        _db.Context.WarehouseReconciliations.Add(new WarehouseReconciliation
        {
            Key = Guid.NewGuid(),
            Code = "CS000009",
            Status = WarehouseReconciliationStatus.Approved,
            WarehouseCode = "02",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            ReferenceDate = new DateTime(2026, 9, 30),
            ReasonKey = Guid.NewGuid(),
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal(
            "A conferência de saldo CS000009 do armazém 02 (30/09/2026) tem data igual ou posterior à data do " +
            "romaneio. Cancele a conferência antes de trocar o fornecedor/armazém.",
            ex.Message);
    }

    [Fact]
    public async Task ContractWithoutBalance_RollsBackEverything()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002", totalVolume: 5_000m);
        var transactions = await _db.Context.StorageTransactions.CountAsync();
        var allocations = await _db.Context.PurchaseContractsAllocations.CountAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 30_000m, "motivo", "tester"));

        Assert.Equal("O volume informado é maior que o saldo disponível do contrato de compra.", ex.Message);
        Assert.Equal(transactions, await _db.Context.StorageTransactions.CountAsync());
        Assert.Equal(allocations, await _db.Context.PurchaseContractsAllocations.CountAsync());
        Assert.Equal(0, await _db.Context.PurchaseTransactionChanges.CountAsync());
        Assert.Equal(30_000m, (await LoadAsync(original.Key)).AvaiableVolumeToAllocate);
        Assert.Equal(0m, (await LoadContractAsync(contract.Key)).AllocatedVolume);
    }

    // ─────────────────────────────── troca parcial ───────────────────────────────

    [Fact]
    public async Task Partial_WithoutInvoice_SplitsWeightsProportionally()
    {
        var original = await SeedOriginalAsync(gross: 31_000m, drying: 600m, cleaning: 400m);
        var own = await SeedContractAsync("PC-001", cardCode: "F0001", warehouseCode: "01");
        var existing = await SeedAllocationAsync(original, own, 10_000m);
        var contract = await SeedContractAsync("PC-002");

        await Service().ExecuteAsync(original.Key, contract.Key, 20_000m, "Parte de outro produtor", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        Assert.Equal(20_000m, change.Quantity);

        foreach (var key in new[] { change.ReturnStorageTransactionKey, change.NewStorageTransactionKey })
        {
            var st = await LoadAsync(key);
            Assert.Equal(20_666.667m, st.GrossWeight);
            Assert.Equal(400m, st.DryingDiscount);
            Assert.Equal(266.667m, st.CleaningDiscount);
            Assert.Equal(0m, st.OthersDicount);
            Assert.Equal(20_000m, st.NetWeight);
        }

        Assert.Equal(0m, (await LoadAsync(original.Key)).AvaiableVolumeToAllocate);

        var kept = await _db.Context.PurchaseContractsAllocations.AsNoTracking().SingleAsync(x => x.Key == existing.Key);
        Assert.Equal((own.Key, original.Key, 10_000m), (kept.PurchaseContractKey, kept.StorageTransactionKey, kept.Volume));
        Assert.Equal(10_000m, (await LoadContractAsync(own.Key)).AllocatedVolume);
        Assert.Equal(20_000m, (await LoadContractAsync(contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Partial_RoundingResidual_ClosesNetExactly()
    {
        // r = 10000/29999,999 → bruto/descontos arredondados a 3 casas; o líquido fecha no saldo.
        var original = await SeedOriginalAsync(gross: 31_000.001m, drying: 600.001m, cleaning: 400.001m);
        var own = await SeedContractAsync("PC-001", cardCode: "F0001", warehouseCode: "01");
        await SeedAllocationAsync(original, own, 19_999.999m);
        var contract = await SeedContractAsync("PC-002");

        await Service().ExecuteAsync(original.Key, contract.Key, 10_000m, "motivo", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        foreach (var key in new[] { change.ReturnStorageTransactionKey, change.NewStorageTransactionKey })
        {
            var st = await LoadAsync(key);
            Assert.Equal(10_000m, st.NetWeight);
            Assert.Equal(st.NetWeight, st.GrossWeight - st.DryingDiscount - st.CleaningDiscount - st.OthersDicount);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Partial_WithInvoice_IsRefused(bool withInvoice)
    {
        var original = await SeedOriginalAsync(invoiceNumber: withInvoice ? "123" : null);
        if (!withInvoice)
            await SeedReconciliationLinkAsync(original);
        var own = await SeedContractAsync("PC-001", cardCode: "F0001", warehouseCode: "01");
        await SeedAllocationAsync(original, own, 10_000m);
        var contract = await SeedContractAsync("PC-002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(original.Key, contract.Key, 20_000m, "motivo", "tester"));

        Assert.Equal(
            "Romaneio com nota fiscal e parte alocada: remova as alocações antes de trocar o fornecedor/armazém.",
            ex.Message);
    }

    [Fact]
    public async Task VolumeBelowBalance_LeavesRemainderOnNewTransaction()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");

        await Service().ExecuteAsync(original.Key, contract.Key, 12_000m, "motivo", "tester");

        var change = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();
        Assert.Equal((30_000m, 12_000m), (change.Quantity, change.AllocatedVolume));
        var neu = await LoadAsync(change.NewStorageTransactionKey);
        Assert.Equal(30_000m, neu.NetWeight);
        Assert.Equal(18_000m, neu.AvaiableVolumeToAllocate);
        Assert.Equal(0m, (await LoadAsync(original.Key)).AvaiableVolumeToAllocate);
        Assert.Equal(12_000m, (await LoadContractAsync(contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task NewTransaction_CanBeChangedAgain()
    {
        var original = await SeedOriginalAsync();
        var contract = await SeedContractAsync("PC-002");
        var back = await SeedContractAsync("PC-001", cardCode: "F0001", warehouseCode: "01");

        await Service().ExecuteAsync(original.Key, contract.Key, 12_000m, "primeira", "tester");
        var first = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync();

        await Service().ExecuteAsync(first.NewStorageTransactionKey, back.Key, 18_000m, "segunda", "tester");

        var second = await _db.Context.PurchaseTransactionChanges.AsNoTracking().SingleAsync(x => x.Reason == "segunda");
        Assert.Equal(first.NewStorageTransactionKey, second.OriginalStorageTransactionKey);
        Assert.Equal(18_000m, second.Quantity);
        Assert.Equal(0m, (await LoadAsync(first.NewStorageTransactionKey)).AvaiableVolumeToAllocate);
        var neu = await LoadAsync(second.NewStorageTransactionKey);
        Assert.Equal(("F0001", "01", 18_000m), (neu.CardCode, neu.WarehouseCode, neu.NetWeight));
        Assert.Equal(18_000m, (await LoadContractAsync(back.Key)).AllocatedVolume);
    }
}
