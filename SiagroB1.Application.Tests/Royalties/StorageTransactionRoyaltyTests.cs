using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.Royalties;

public class StorageTransactionRoyaltyTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsConfirmedService Confirmed() => new(
        _db,
        new FakeStringLocalizer<Resource>(),
        new ShipmentReleasesRecalculateShippedService(_db.Context),
        new ShipmentReleaseMovementGuardService(_db.Context),
        NullLogger<StorageTransactionsConfirmedService>.Instance);

    private async Task<StorageTransaction> SeedReceiptAsync(
        bool controlsRoyalties,
        StorageTransactionsStatus status = StorageTransactionsStatus.Pending,
        RoyaltySituation? situation = null)
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = controlsRoyalties });
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "P0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = status,
            TransactionOrigin = TransactionCode.StorageTransaction,
            GrossWeight = 30_000m,
            NetWeight = 30_000m,
            RoyaltySituation = situation,
            RoyaltyOriginCode = situation is null ? null : "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        await _db.SaveChangesAsync();
        return st;
    }

    private async Task AddActiveWriteOffAsync(StorageTransaction st)
    {
        var writeOff = new RoyaltyWriteOff
        {
            ItsId = "ITS-1", TaxId = "12345678909", Name = "Produtor", Situation = RoyaltySituation.Declared,
            WriteOffDate = DateTime.Today,
        };
        writeOff.Lines.Add(new RoyaltyWriteOffLine { StorageTransactionKey = st.Key, Weight = 1_000m });
        _db.Context.RoyaltyWriteOffs.Add(writeOff);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Confirming_a_controlled_receipt_without_situation_is_refused()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: true);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Confirmed().ExecuteAsync(st, "tester"));

        Assert.Contains("situação de royalty", ex.Message);
        Assert.Equal(StorageTransactionsStatus.Pending, st.TransactionStatus);
    }

    [Fact]
    public async Task Confirming_a_controlled_receipt_with_situation_works()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: true, situation: RoyaltySituation.Conventional);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
    }

    [Fact]
    public async Task Confirming_a_receipt_of_an_uncontrolled_item_ignores_royalties()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: false);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
    }

    [Fact]
    public void Copy_carries_the_royalty_fields()
    {
        var original = new StorageTransaction
        {
            CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "ARM01",
            RoyaltySituation = RoyaltySituation.Tested, RoyaltyTestCode = "T-9",
            RoyaltyOriginCode = "P0002", RoyaltyOriginName = "Origem",
        };

        var copy = StorageTransactionCopyFactory.CreateFrom(original, "tester");

        Assert.Equal(RoyaltySituation.Tested, copy.RoyaltySituation);
        Assert.Equal("T-9", copy.RoyaltyTestCode);
        Assert.Equal("P0002", copy.RoyaltyOriginCode);
        Assert.Equal("Origem", copy.RoyaltyOriginName);
    }

    [Fact]
    public async Task Cancel_is_refused_while_an_active_write_off_exists()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var service = new StorageTransactionsCancelService(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(st.Key, "tester"));

        Assert.Contains("baixa ITS ativa (ITS-1)", ex.Message);
    }

    [Fact]
    public async Task Reverse_is_refused_while_an_active_write_off_exists()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(st.Key, "tester"));

        Assert.Contains("baixa ITS ativa", ex.Message);
    }

    [Fact]
    public async Task Cancelled_write_off_no_longer_blocks_the_reversal_guard()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var writeOff = _db.Context.RoyaltyWriteOffs.Single();
        writeOff.Status = RoyaltyWriteOffStatus.Cancelled;
        await _db.SaveChangesAsync();

        Assert.Null(await Record.ExceptionAsync(() =>
            Services.Royalties.RoyaltyRules.EnsureNoActiveWriteOffAsync(_db.Context, st.Key)));
    }

    private async Task<StorageTransaction> SeedPurchaseAsync(
        bool participantWarehouse, RoyaltySituation? situation = null,
        TransactionCode origin = TransactionCode.StorageTransaction)
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _db.Context.WarehouseComplements.Add(new WarehouseComplement { WarehouseCode = "ARMP", IsParticipant = participantWarehouse });
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "C1", CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARMP", WarehouseName = "Armazém do carregamento", BranchCode = "01",
            TransactionType = StorageTransactionType.Purchase, TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionOrigin = origin, GrossWeight = 30_000m, NetWeight = 30_000m,
            RoyaltySituation = situation, RoyaltyOriginCode = "OUTRO",
        };
        _db.Context.StorageTransactions.Add(st);
        await _db.SaveChangesAsync();
        return st;
    }

    private static readonly Dictionary<string, string> Partners = new() { ["P0002"] = "Produtor Origem" };

    private StorageTransactionsCreateService Create() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(Partners),
        new FakeItemService(new() { ["SOJA"] = "Soja" }),
        new FakeWarehouseService(new() { ["ARM01"] = "Armazém 01" }),
        new ShipmentReleasesRecalculateShippedService(_db.Context),
        new ShipmentReleaseMovementGuardService(_db.Context),
        NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsUpdateService Update() => new(
        _db.Context,
        _db,
        new FakeBusinessPartnerService(Partners),
        new FakeItemService(new() { ["SOJA"] = "Soja" }),
        new FakeWarehouseService(new() { ["ARM01"] = "Armazém 01" }),
        NullLogger<StorageTransactionsUpdateService>.Instance);

    private static StorageTransaction NewDraft(StorageTransactionType type) => new()
    {
        Key = Guid.NewGuid(), CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        WarehouseCode = "ARM01", BranchCode = "01", TransactionType = type,
        TransactionStatus = StorageTransactionsStatus.Pending,
        RoyaltySituation = RoyaltySituation.Declared, RoyaltyOriginCode = "P0002",
    };

    [Fact]
    public async Task Create_resolves_the_royalty_origin_name_from_the_partner_on_a_receipt()
    {
        var created = await Create().ExecuteAsync(NewDraft(StorageTransactionType.Receipt), "tester");

        Assert.Equal("Produtor Origem", created.RoyaltyOriginName);
    }

    [Fact]
    public async Task Create_uses_the_warehouse_name_as_royalty_origin_name_on_a_purchase()
    {
        var created = await Create().ExecuteAsync(NewDraft(StorageTransactionType.Purchase), "tester");

        Assert.Equal("Armazém 01", created.RoyaltyOriginName);
    }

    [Fact]
    public async Task Update_resolves_the_royalty_origin_name_from_the_partner_on_a_receipt()
    {
        var created = await Create().ExecuteAsync(NewDraft(StorageTransactionType.Receipt), "tester");
        var edit = NewDraft(StorageTransactionType.Receipt);
        edit.Key = created.Key;
        edit.Code = created.Code;

        await Update().ExecuteAsync(created.Key, edit, "tester");
        var updated = _db.Context.StorageTransactions.Single(x => x.Key == created.Key);

        Assert.Equal("Produtor Origem", updated.RoyaltyOriginName);
    }

    [Fact]
    public async Task Update_uses_the_warehouse_name_as_royalty_origin_name_on_a_purchase()
    {
        var created = await Create().ExecuteAsync(NewDraft(StorageTransactionType.Purchase), "tester");
        var edit = NewDraft(StorageTransactionType.Purchase);
        edit.Key = created.Key;
        edit.Code = created.Code;

        await Update().ExecuteAsync(created.Key, edit, "tester");
        var updated = _db.Context.StorageTransactions.Single(x => x.Key == created.Key);

        Assert.Equal("Armazém 01", updated.RoyaltyOriginName);
    }

    [Fact]
    public async Task Purchase_at_a_participant_warehouse_defaults_the_situation_and_the_origin()
    {
        var st = await SeedPurchaseAsync(participantWarehouse: true);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
        Assert.Equal(RoyaltySituation.ParticipantWarehouse, st.RoyaltySituation);
        Assert.Equal("ARMP", st.RoyaltyOriginCode);
        Assert.Equal("Armazém do carregamento", st.RoyaltyOriginName);
    }

    [Fact]
    public async Task Purchase_at_a_farm_requires_the_situation()
    {
        var st = await SeedPurchaseAsync(participantWarehouse: false);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Confirmed().ExecuteAsync(st, "tester"));

        Assert.Contains("situação de royalty", ex.Message);
    }

    [Fact]
    public async Task Purchase_keeps_the_informed_situation()
    {
        var st = await SeedPurchaseAsync(participantWarehouse: true, situation: RoyaltySituation.Declared);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(RoyaltySituation.Declared, st.RoyaltySituation);
    }

    [Fact]
    public async Task Warehouse_reconciliation_purchase_is_exempt()
    {
        var st = await SeedPurchaseAsync(participantWarehouse: false, origin: TransactionCode.WarehouseReconciliation);

        await Confirmed().ExecuteAsync(st, "tester", CommitMode.Auto, isShipmentTransaction: true);

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
        Assert.Null(st.RoyaltySituation);
    }
}
