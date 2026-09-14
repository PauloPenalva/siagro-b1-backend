using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O extrato de movimentação precisa contar Sobra de armazém (14) como entrada e Perda de
/// armazém (13) como saída, com o mesmo rótulo usado no resto do sistema
/// (<see cref="SiagroB1.Reports.Helpers.StorageStatementReportHelper"/>) — antes desta correção
/// o serviço não reconhecia os dois tipos e imprimia o nome cru do enum.
/// </summary>
public class StorageStatementReportServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<StorageTransaction> SeedAsync(StorageTransactionType type, decimal netWeight)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"AJ-{Guid.NewGuid():N}"[..10],
            CardCode = "ARM-T",
            CardName = "Armazém Terceiro",
            ItemCode = "SOJA",
            ItemName = "Soja em grãos",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM-T",
            BranchCode = "01",
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.WarehouseReconciliation,
            TransactionDate = DateTime.Today,
            GrossWeight = netWeight,
            NetWeight = netWeight,
        };

        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    [Fact]
    public async Task Warehouse_gain_counts_as_entrada_with_the_shared_label()
    {
        await SeedAsync(StorageTransactionType.WarehouseGain, 25m);

        var (rows, header) = await new StorageStatementReportService(_db)
            .GetReportAsync(new StorageStatementReportFilter(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(25m, row.QtyEntrada);
        Assert.Equal(0m, row.QtySaida);
        Assert.Equal("Sobra Armazém", row.TransactionTypeName);
        Assert.Equal(25m, header.Single().TotalEntrada);
    }

    [Fact]
    public async Task Warehouse_loss_counts_as_saida_with_the_shared_label()
    {
        await SeedAsync(StorageTransactionType.WarehouseLoss, 15m);

        var (rows, header) = await new StorageStatementReportService(_db)
            .GetReportAsync(new StorageStatementReportFilter(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(0m, row.QtyEntrada);
        Assert.Equal(15m, row.QtySaida);
        Assert.Equal("Perda Armazém", row.TransactionTypeName);
        Assert.Equal(15m, header.Single().TotalSaida);
    }

    [Fact]
    public async Task Purchase_still_uses_the_shared_label_instead_of_the_raw_enum_name()
    {
        await SeedAsync(StorageTransactionType.Purchase, 100m);

        var (rows, _) = await new StorageStatementReportService(_db)
            .GetReportAsync(new StorageStatementReportFilter(), CancellationToken.None);

        Assert.Equal("Compra - Entrada", Assert.Single(rows).TransactionTypeName);
    }
}
