// SiagroB1.Application.Tests/Reports/RoyaltyPendingReportServiceTests.cs
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class RoyaltyPendingReportServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private void Receipt(string code, string card, RoyaltySituation situation, decimal net,
        StorageTransactionType type = StorageTransactionType.Receipt)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = card, CardName = "Nome " + card, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = new DateTime(2026, 10, 1),
            NetWeight = net, RoyaltySituation = situation, RoyaltyOriginCode = card, RoyaltyOriginName = "Nome " + card,
        });
    }

    private static RoyaltyPendingReportService Service(IUnitOfWork db) =>
        new(db, null!, new ConfigurationBuilder().Build());

    private static RoyaltyPendingRequest Request() =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) };

    [Fact]
    public async Task Rows_are_grouped_by_situation_then_partner_with_subtotals()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        Receipt("R1", "P2", RoyaltySituation.Declared, 100m);
        Receipt("R2", "P1", RoyaltySituation.Declared, 50m);
        Receipt("R3", "P1", RoyaltySituation.Conventional, 30m);
        await _db.SaveChangesAsync();

        var data = await Service(_db).BuildAsync(Request());

        Assert.Equal(
            ["Item:R2", "Partner:", "Item:R1", "Partner:", "Situation:", "Item:R3", "Partner:", "Situation:"],
            data.Rows.Select(r => $"{r.Kind}:{r.Code}"));
        Assert.Equal(150m, data.Rows[4].Balance);   // subtotal Declarada
        Assert.Equal(180m, data.TotalBalance);
        Assert.Equal(3, data.ItemCount);
    }

    [Fact]
    public async Task A_purchase_row_is_labelled_as_compra()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        Receipt("C1", "P1", RoyaltySituation.Declared, 40m, StorageTransactionType.Purchase);
        await _db.SaveChangesAsync();

        var data = await Service(_db).BuildAsync(Request());

        var row = Assert.Single(data.Rows, r => r.Code == "C1");
        Assert.Equal("Compra", row.Type);
    }

    private void WriteOff(string receiptCode, decimal weight, string itsId, DateTime date,
        RoyaltyWriteOffStatus status = RoyaltyWriteOffStatus.Active)
    {
        var key = _db.Context.StorageTransactions.Local.Single(x => x.Code == receiptCode).Key;
        var w = new RoyaltyWriteOff
        {
            ItsId = itsId, TaxId = "12345678909", Name = "Fulano " + itsId, Situation = RoyaltySituation.Declared,
            Status = status, WriteOffDate = date,
        };
        w.Lines.Add(new RoyaltyWriteOffLine { StorageTransactionKey = key, Weight = weight });
        _db.Context.RoyaltyWriteOffs.Add(w);
    }

    private async Task SeedScopesAsync()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        Receipt("R1", "P1", RoyaltySituation.Declared, 100m);   // totalmente baixado
        Receipt("R2", "P1", RoyaltySituation.Declared, 50m);    // parcial
        Receipt("R3", "P1", RoyaltySituation.Declared, 30m);    // sem baixa
        WriteOff("R1", 60m, "ITS-B", new DateTime(2026, 10, 5));
        WriteOff("R1", 40m, "ITS-A", new DateTime(2026, 10, 2));
        WriteOff("R1", 10m, "ITS-X", new DateTime(2026, 10, 3), RoyaltyWriteOffStatus.Cancelled);
        WriteOff("R2", 20m, "ITS-C", new DateTime(2026, 10, 4));
        await _db.SaveChangesAsync();
    }

    private static RoyaltyPendingRequest Request(RoyaltyBalanceScope scope)
    {
        var r = Request();
        r.Scope = scope.ToString();
        return r;
    }

    [Fact]
    public async Task Pending_scope_by_default_lists_open_receipts_with_their_write_offs()
    {
        await SeedScopesAsync();

        var data = await Service(_db).BuildAsync(Request());

        Assert.Equal(["Item:R2", "WriteOff:", "Item:R3", "Partner:", "Situation:"],
            data.Rows.Select(r => $"{r.Kind}:{(r.Kind == RoyaltyPendingRowKind.Item ? r.Code : "")}"));
        Assert.Equal(2, data.ItemCount);
    }

    [Fact]
    public async Task Written_off_scope_shows_only_fully_written_off_receipts()
    {
        await SeedScopesAsync();

        var data = await Service(_db).BuildAsync(Request(RoyaltyBalanceScope.WrittenOff));

        Assert.Equal(["R1"], data.Rows.Where(r => r.Kind == RoyaltyPendingRowKind.Item).Select(r => r.Code));
        Assert.Equal(0m, data.TotalBalance);
    }

    [Fact]
    public async Task All_scope_places_active_write_offs_right_after_their_receipt_and_totals_ignore_them()
    {
        await SeedScopesAsync();

        var data = await Service(_db).BuildAsync(Request(RoyaltyBalanceScope.All));

        Assert.Equal(
            ["Item:R1", "WriteOff:", "WriteOff:", "Item:R2", "WriteOff:", "Item:R3", "Partner:", "Situation:"],
            data.Rows.Select(r => $"{r.Kind}:{(r.Kind == RoyaltyPendingRowKind.Item ? r.Code : "")}"));
        var writeOffs = data.Rows.Where(r => r.Kind == RoyaltyPendingRowKind.WriteOff).ToList();
        Assert.Equal(["ITS-A", "ITS-B", "ITS-C"], writeOffs.Select(r => r.Detail.Split(' ')[2]));   // data, depois RowId
        Assert.Equal("02/10/2026", writeOffs[0].Date);
        Assert.Contains("12345678909", writeOffs[0].Detail);
        Assert.Contains("Fulano ITS-A", writeOffs[0].Detail);
        Assert.Equal(40m, writeOffs[0].WrittenOff);
        Assert.False(writeOffs[0].IsSubtotal);
        Assert.True(writeOffs[0].IsWriteOff);
        Assert.Equal(3, data.ItemCount);
        Assert.Equal(180m, data.TotalNetWeight);
        Assert.Equal(120m, data.TotalWrittenOff);
        Assert.Equal(60m, data.TotalBalance);
        Assert.Equal(120m, data.Rows[^2].WrittenOff);   // subtotal do parceiro soma só os romaneios
    }
}
