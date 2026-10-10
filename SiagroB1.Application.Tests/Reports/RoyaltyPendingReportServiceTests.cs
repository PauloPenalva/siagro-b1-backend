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
}
