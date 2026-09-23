using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.StorageAddresses;

public class StorageAddressesGetServiceTests
{
    /// <summary>
    /// A lista de lotes e o value help de lote da pesagem leem o QueryAll. Ele carregava todos os
    /// romaneios de cada lote só para calcular o saldo: o plano varria STORAGE_TRANSACTIONS inteira,
    /// e sem READ_COMMITTED_SNAPSHOT qualquer romaneio travado, até de outro lote, segurava a tela
    /// até o timeout de 30 s. O saldo tem de sair somado do próprio banco.
    /// </summary>
    [Fact]
    public void QueryAll_computes_the_balance_in_the_database()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none",
                b => b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options);

        var service = new StorageAddressesGetService(
            new UnitOfWork(context), new TestLogger<StorageAddressesGetService>());

        var sql = service.QueryAll().ToQueryString();

        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[STORAGE_TRANSACTIONS]", sql);
    }

    /// <summary>
    /// Somar no banco não bastou: sem as colunas da soma no índice, o plano seguia varrendo a
    /// tabela inteira (2.388 leituras) e um romaneio travado de outro lote ainda bloqueava a lista.
    /// Com o índice cobrindo a soma vira Index Seek (12 leituras) e só lê os lotes da página.
    /// </summary>
    [Fact]
    public void Storage_address_index_on_transactions_covers_the_balance_sum()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

        // IncludeProperties só existe no modelo de design; o de runtime não guarda essa configuração.
        var index = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StorageTransaction))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(StorageTransaction.StorageAddressCode)]));

        Assert.Equal(
            [nameof(StorageTransaction.TransactionType), nameof(StorageTransaction.TransactionStatus), nameof(StorageTransaction.NetWeight)],
            index.GetIncludeProperties());
    }

    [Fact]
    public async Task QueryAll_keeps_the_balance_rule_and_every_mapped_field()
    {
        var db = TestDb.CreateUnitOfWork();

        var withMovements = new StorageAddress
        {
            Code = "L001",
            BranchCode = "3",
            DocNumberKey = Guid.NewGuid(),
            Description = "Lote com movimento",
            CardCode = "C0001",
            CardName = "Cliente",
            ItemCode = "SOJA",
            ItemName = "Soja",
            WarehouseCode = "01",
            WarehouseName = "Armazém",
            UoM = "KG",
            OwnershipType = StorageOwnershipType.OwnedInOurCustody,
            Status = StorageAddressStatus.Open,
            ProcessingCostCode = "PC01",
            PurchaseContractKey = Guid.NewGuid(),
            TransactionOrigin = TransactionCode.WeighingTicket,
            CreatedBy = "admin",
        };

        var empty = new StorageAddress
        {
            Code = "L002",
            Description = "Lote sem movimento",
            CardCode = "C0002",
            ItemCode = "MILHO",
            WarehouseCode = "01",
            UoM = "KG",
        };

        db.Context.StorageAddresses.AddRange(withMovements, empty);

        void Movement(StorageTransactionType type, StorageTransactionsStatus status, decimal netWeight) =>
            db.Context.StorageTransactions.Add(new StorageTransaction
            {
                Key = Guid.NewGuid(),
                StorageAddressCode = "L001",
                TransactionType = type,
                TransactionStatus = status,
                NetWeight = netWeight,
                CardCode = "C0001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                WarehouseCode = "01",
            });

        // Entram no saldo
        Movement(StorageTransactionType.Receipt, StorageTransactionsStatus.Confirmed, 1000m);
        Movement(StorageTransactionType.ShipmentReleased, StorageTransactionsStatus.Invoiced, 500m);
        Movement(StorageTransactionType.Shipment, StorageTransactionsStatus.Confirmed, 300m);
        Movement(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Invoiced, 100m);
        Movement(StorageTransactionType.TechnicalLoss, StorageTransactionsStatus.Confirmed, 50m);
        Movement(StorageTransactionType.Receipt, StorageTransactionsStatus.Confirmed, 0.4m);
        // 12 com lote = estorno de troca de liberação (GAC-1177): credita como um recebimento.
        Movement(StorageTransactionType.SalesShipmentReturn, StorageTransactionsStatus.Confirmed, 200m);

        // Ficam de fora: status que não conta e tipo que não mexe no lote
        Movement(StorageTransactionType.Receipt, StorageTransactionsStatus.Pending, 999m);
        Movement(StorageTransactionType.Shipment, StorageTransactionsStatus.Cancelled, 777m);
        Movement(StorageTransactionType.Adjustment, StorageTransactionsStatus.Confirmed, 12345m);

        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var service = new StorageAddressesGetService(db, new TestLogger<StorageAddressesGetService>());

        var rows = await service.QueryAll().OrderBy(x => x.Code).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1250m, rows[0].Balance); // 1000 + 500 - 300 - 100 - 50 + 0,4 + 200, arredondado
        Assert.Equal(0m, rows[1].Balance);

        var scalarProperties = db.Context.Model.FindEntityType(typeof(StorageAddress))!
            .GetProperties()
            .Where(p => p.PropertyInfo != null)
            .ToList();

        foreach (var (expected, actual) in new[] { (withMovements, rows[0]), (empty, rows[1]) })
        foreach (var property in scalarProperties)
        {
            Assert.True(
                Equals(property.PropertyInfo!.GetValue(expected), property.PropertyInfo.GetValue(actual)),
                $"{expected.Code}.{property.Name} não foi preservado pelo QueryAll.");
        }
    }

    /// <summary>
    /// GAC-1181 fase 2, round 1 de correção: a projeção manual do QueryAll não incluía
    /// <see cref="StorageAddress.Nature"/>, então todo lote voltava da LISTA como
    /// <see cref="StorageAddressNature.Regular"/> independentemente do que estava gravado — o
    /// teste "every mapped field" acima não pegava isso porque nenhum dos dois lotes de exemplo
    /// usa <see cref="StorageAddressNature.Transshipment"/> (default C# e valor gravado coincidem
    /// em Regular por acaso). Este teste grava um lote de cada natureza para provar a diferença.
    /// </summary>
    [Fact]
    public async Task QueryAll_keeps_the_nature_of_each_lot()
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.StorageAddresses.AddRange(
            new StorageAddress
            {
                Code = "L003",
                Description = "Lote regular",
                CardCode = "C0001",
                ItemCode = "SOJA",
                WarehouseCode = "01",
                UoM = "KG",
                Nature = StorageAddressNature.Regular,
            },
            new StorageAddress
            {
                Code = "L004",
                Description = "Lote de transbordo",
                CardCode = "C0001",
                ItemCode = "SOJA",
                WarehouseCode = "01",
                UoM = "KG",
                Nature = StorageAddressNature.Transshipment,
            });

        await db.SaveChangesAsync();

        var service = new StorageAddressesGetService(db, new TestLogger<StorageAddressesGetService>());

        var natureByCode = await service.QueryAll().ToDictionaryAsync(x => x.Code!, x => x.Nature);

        Assert.Equal(StorageAddressNature.Regular, natureByCode["L003"]);
        Assert.Equal(StorageAddressNature.Transshipment, natureByCode["L004"]);
    }
}
