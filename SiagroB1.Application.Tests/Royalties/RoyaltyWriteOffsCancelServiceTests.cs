using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyWriteOffsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly StorageTransaction _receipt;

    public RoyaltyWriteOffsCancelServiceTests()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = 1_000m,
            RoyaltySituation = RoyaltySituation.Declared, RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(_receipt);
        _db.Context.SaveChanges();
    }

    private Task<RoyaltyWriteOff> CreateAsync(string itsId, decimal weight) =>
        new RoyaltyWriteOffsCreateService(_db).ExecuteAsync(
            new RoyaltyWriteOffInput(DateTime.Today, itsId, "52998224725", "Produtor", null,
                [new RoyaltyWriteOffLineInput(_receipt.Key, weight)]), "tester");

    private async Task<decimal> BalanceAsync() =>
        (await RoyaltyBalanceQuery.Build(_db.Context).SingleAsync(x => x.StorageTransactionKey == _receipt.Key)).Balance;

    [Fact]
    public async Task Cancel_returns_the_balance_and_records_who()
    {
        var w = await CreateAsync("ITS-1", 1_000m);

        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        var saved = await _db.Context.RoyaltyWriteOffs.AsNoTracking().SingleAsync(x => x.Key == w.Key);
        Assert.Equal(RoyaltyWriteOffStatus.Cancelled, saved.Status);
        Assert.Equal("canceler", saved.CanceledBy);
        Assert.Equal(1_000m, await BalanceAsync());
    }

    [Fact]
    public async Task Cancel_frees_the_its_id()
    {
        var w = await CreateAsync("ITS-1", 500m);
        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        await CreateAsync("ITS-1", 500m);

        Assert.Equal(500m, await BalanceAsync());
    }

    [Fact]
    public async Task Cancelling_twice_is_refused()
    {
        var w = await CreateAsync("ITS-1", 500m);
        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler"));

        Assert.Contains("já está cancelada", ex.Message);
    }
}
