using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketHedgeTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketBrokerStatementServiceTests
{
    [Fact]
    public async Task Create_StampsCreatedByAndUtcAt_AndNormalizesDate()
    {
        var db = await SeedCounterpartiesAsync();
        var input = Statement(1, new DateTime(2026, 11, 5, 14, 30, 0));

        var created = await StatementService(db).CreateAsync(input, "ana");

        Assert.Equal("ana", created.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
        Assert.True(Math.Abs((DateTime.UtcNow - created.CreatedAt).TotalMinutes) < 2);
        Assert.Equal(new DateTime(2026, 11, 5), created.Date);
    }

    [Fact]
    public async Task Create_ClientCannotForgeCreatedByOrAt()
    {
        var db = await SeedCounterpartiesAsync();
        var input = Statement(1);
        input.CreatedBy = "forjado";
        input.CreatedAt = new DateTime(2000, 1, 1);

        var created = await StatementService(db).CreateAsync(input, "ana");

        Assert.Equal("ana", created.CreatedBy);
        Assert.True(created.CreatedAt.Year >= 2026);
    }

    [Fact]
    public async Task Create_BankAccount_Rejected()
    {
        var db = await SeedCounterpartiesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => StatementService(db).CreateAsync(Statement(2), "ana"));
        Assert.Contains("corretora", ex.Message);
    }

    [Fact]
    public async Task Create_UnknownInactiveAccountOrInactiveBroker_Rejected()
    {
        var db = await SeedCounterpartiesAsync();
        var service = StatementService(db);
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Statement(999), "ana"));

        var account = await db.Context.MarketCounterpartyAccounts.FindAsync(1);
        account!.Active = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Statement(1), "ana"));

        account.Active = true;
        (await db.Context.MarketCounterparties.FindAsync(1))!.Active = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Statement(1), "ana"));
    }

    [Fact]
    public async Task Create_DuplicateAccountAndDate_Rejected_OtherDateAllowed()
    {
        var db = await SeedCounterpartiesAsync();
        var service = StatementService(db);
        await service.CreateAsync(Statement(1), "ana");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Statement(1), "ana"));
        Assert.Contains("extrato", ex.Message);
        Assert.NotNull(await service.CreateAsync(Statement(1, new DateTime(2026, 11, 6)), "ana"));
    }

    [Fact]
    public async Task Create_DefaultDate_Rejected()
    {
        var db = await SeedCounterpartiesAsync();
        var input = Statement(1);
        input.Date = default;

        await Assert.ThrowsAsync<BusinessException>(() => StatementService(db).CreateAsync(input, "ana"));
    }

    [Fact]
    public async Task Create_NegativeAdjustmentAllowed_NegativeMarginRejected()
    {
        var db = await SeedCounterpartiesAsync();
        var service = StatementService(db);

        var negative = Statement(1);
        negative.MarginRequired = -1m;
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(negative, "ana"));
        var negativeDeposit = Statement(1);
        negativeDeposit.MarginDeposited = -0.01m;
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(negativeDeposit, "ana"));

        var ok = await service.CreateAsync(Statement(1), "ana");
        Assert.Equal(-150.5m, ok.DailyAdjustment);
    }

    [Fact]
    public async Task Update_PreservesCreatedStamp_AndRejectsDuplicateDate()
    {
        var db = await SeedCounterpartiesAsync();
        var service = StatementService(db);
        var first = await service.CreateAsync(Statement(1), "ana");
        var second = await service.CreateAsync(Statement(1, new DateTime(2026, 11, 6)), "ana");
        var firstCreatedAt = first.CreatedAt;
        db.Context.ChangeTracker.Clear();

        var edit = Statement(1, new DateTime(2026, 11, 5));
        edit.MarginRequired = 2000m;
        edit.CreatedBy = "outro";
        edit.CreatedAt = new DateTime(2001, 1, 1);
        await service.UpdateAsync(first.Id, edit, "bia");

        var saved = await db.Context.MarketBrokerStatements.AsNoTracking().SingleAsync(s => s.Id == first.Id);
        Assert.Equal(2000m, saved.MarginRequired);
        Assert.Equal("ana", saved.CreatedBy);
        Assert.Equal(firstCreatedAt, saved.CreatedAt);

        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(second.Id, Statement(1, new DateTime(2026, 11, 5)), "bia"));
        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(999, Statement(1), "bia"));
    }
}
