using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Romaneio só numera automaticamente (nunca aceita código digitado) e não se exclui, só se cancela.
/// </summary>
public class StorageTransactionsNumberingTests
{
    private const string CardCode = "C0001";
    private const string ItemCode = "TRIGO";
    private const string WarehouseCode = "ARM01";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsCreateService CreateService(FakeDocNumberSequenceService numbers) =>
        new(_db,
            numbers,
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Trigo" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    [Fact]
    public async Task StorageTransactionsCreate_IgnoresTypedCode()
    {
        var numbers = new FakeDocNumberSequenceService();
        var seriesKey = Guid.NewGuid();
        var entity = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            DocNumberKey = seriesKey,
            Code = "X",
            CardCode = CardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = WarehouseCode,
            BranchCode = "01",
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Pending,
        };

        await CreateService(numbers).ExecuteAsync(entity, "tester");

        var request = Assert.Single(numbers.Requests);
        Assert.Equal(TransactionCode.StorageTransaction, request.TransactionCode);
        Assert.Equal(seriesKey, request.DocNumberKey);
        Assert.Null(request.TypedCode);
        Assert.Equal(50, request.MaxLength);
        Assert.NotEqual("X", entity.Code);
        Assert.StartsWith("ST-", entity.Code);

        // O predicado de duplicidade enxerga romaneios já gravados.
        Assert.True(await request.ExistsAsync(entity.Code!));
        Assert.False(await request.ExistsAsync("NOPE"));
    }

    private sealed class SeriesStub : SiagroB1.Application.Services.DocNumbers.DocNumberSequenceService
    {
        public static readonly Guid DefaultKey = Guid.NewGuid();
        public static readonly Guid InactiveKey = Guid.NewGuid();
        public static readonly Guid OtherTypeKey = Guid.NewGuid();

        public SeriesStub() : base(null!) { }

        public override Task<SiagroB1.Application.Services.DocNumbers.DocNumberSeries?> GetSeriesAsync(Guid key) =>
            Task.FromResult<SiagroB1.Application.Services.DocNumbers.DocNumberSeries?>(key switch
            {
                _ when key == DefaultKey => new(key, TransactionCode.StorageTransaction, "Automático", false, false),
                _ when key == InactiveKey => new(key, TransactionCode.StorageTransaction, "Velha", false, true),
                _ when key == OtherTypeKey => new(key, TransactionCode.SalesInvoice, "Outra", false, false),
                _ => null,
            });

        public override Task<string> GetDocNumber(Guid docNumberKey) => Task.FromResult("R000001");

        public override Task<Guid> GetKeyByTransactionCode(TransactionCode transactionCode) => Task.FromResult(DefaultKey);
    }

    private StorageTransactionsCreateService CreateService(SeriesStub numbers) =>
        new(_db, numbers,
            new FakeBusinessPartnerService(new() { [CardCode] = "Cliente Teste" }),
            new FakeItemService(new() { [ItemCode] = "Trigo" }),
            new FakeWarehouseService(new() { [WarehouseCode] = "Armazém 1" }),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private static StorageTransaction NewEntity(Guid? key) => new()
    {
        Key = Guid.NewGuid(), DocNumberKey = key, CardCode = CardCode, ItemCode = ItemCode,
        UnitOfMeasureCode = "KG", WarehouseCode = WarehouseCode, BranchCode = "01",
        TransactionType = StorageTransactionType.Receipt, TransactionStatus = StorageTransactionsStatus.Pending,
    };

    [Fact]
    public async Task InheritedInactiveSeries_FallsBackToDefaultSeries()
    {
        var entity = NewEntity(SeriesStub.InactiveKey);

        await CreateService(new SeriesStub()).ExecuteAsync(entity, "tester");

        Assert.Equal(SeriesStub.DefaultKey, entity.DocNumberKey);
        Assert.Equal("R000001", entity.Code);
    }

    [Fact]
    public async Task SeriesRefusal_IsNotWrappedInGenericError()
    {
        var service = CreateService(new SeriesStub());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.ExecuteAsyncWithTransaction(NewEntity(SeriesStub.OtherTypeKey), "tester"));

        Assert.Equal("A série de numeração escolhida não pertence a este documento.", ex.Message);
    }

    [Fact]
    public void StorageTransactionsDelete_AlwaysRefusesWithBusinessMessage()
    {
        var controller = new StorageTransactionsController(null!, null!, null!);

        var result = controller.Delete(Guid.NewGuid());

        var bad = Assert.IsType<Microsoft.AspNetCore.OData.Results.BadRequestODataResult>(result);
        Assert.Equal("Romaneio não pode ser excluído. Use o cancelamento.", bad.Error.Message);
    }
}
