using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketHedgeTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketCounterpartyServiceTests
{
    [Fact]
    public async Task Counterparty_BlankName_Rejected()
    {
        var db = await SeedCounterpartiesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => CounterpartyService(db).CreateAsync(Broker("   ")));
    }

    [Fact]
    public async Task Counterparty_NameTrimmed_AndDuplicateIgnoringCaseRejected()
    {
        var db = await SeedCounterpartiesAsync();
        var service = CounterpartyService(db);

        var created = await service.CreateAsync(Broker("  Nova Corretora  "));
        Assert.Equal("Nova Corretora", created.Name);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Broker("NOVA CORRETORA")));
        Assert.Contains("nome", ex.Message);
    }

    [Fact]
    public async Task Counterparty_Update_SameNameOfItself_Allowed_OtherName_Rejected()
    {
        var db = await SeedCounterpartiesAsync();
        var service = CounterpartyService(db);

        Assert.NotNull(await service.UpdateAsync(1, Broker("XP Corretora")));

        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(1, Broker("banco alfa")));
    }

    [Fact]
    public async Task Counterparty_DeepCreate_ValidatesAccounts_AndSavesNothingWhenOneIsInvalid()
    {
        var db = await SeedCounterpartiesAsync();
        var service = CounterpartyService(db);
        var bad = Broker("Com contas");
        bad.Accounts = [new() { CounterpartyId = 0, Code = "A-1" }, new() { CounterpartyId = 0, Code = "  " }];

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(bad));
        Assert.False(await db.Context.MarketCounterparties.AnyAsync(c => c.Name == "Com contas"));

        var dup = Broker("Duplicadas");
        dup.Accounts = [new() { CounterpartyId = 0, Code = "A-1" }, new() { CounterpartyId = 0, Code = "a-1" }];
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(dup));
        Assert.Contains("conta", ex.Message);

        var good = Broker("Boa");
        good.Accounts = [new() { CounterpartyId = 0, Code = " A-1 " }];
        var created = await service.CreateAsync(good);
        Assert.Equal("A-1", Assert.Single(created.Accounts).Code);
    }

    [Fact]
    public async Task Counterparty_TypeChange_AllowedWhileUnused_RefusedOnceAccountIsUsed()
    {
        var db = await SeedCounterpartiesAsync();
        var service = CounterpartyService(db);
        var asBank = Broker("XP Corretora");
        asBank.Type = MarketCounterpartyType.Bank;
        await service.UpdateAsync(1, asBank);

        db.Context.ChangeTracker.Clear();
        await UseAccountAsync(db, "trade", 1);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(1, Broker("XP Corretora")));
        Assert.Contains("tipo", ex.Message);
    }

    [Fact]
    public async Task Counterparty_Update_IgnoresAccountsInPayload()
    {
        var db = await SeedCounterpartiesAsync();
        var payload = Broker("XP Corretora");
        payload.Accounts = [new() { CounterpartyId = 1, Code = "NOVA" }];

        await CounterpartyService(db).UpdateAsync(1, payload);

        Assert.Equal(["C-001"], await db.Context.MarketCounterpartyAccounts.AsNoTracking()
            .Where(a => a.CounterpartyId == 1).Select(a => a.Code).ToListAsync());
    }

    [Theory]
    [InlineData("trade")]
    [InlineData("fx")]
    [InlineData("efp")]
    [InlineData("statement")]
    public async Task Counterparty_Delete_RefusedWhenAnyAccountIsUsed(string table)
    {
        var db = await SeedCounterpartiesAsync();
        await UseAccountAsync(db, table, 1);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => CounterpartyService(db).DeleteAsync(1));
        Assert.Contains("inative", ex.Message);
        Assert.True(await db.Context.MarketCounterparties.AnyAsync(c => c.Id == 1));
    }

    [Fact]
    public async Task Counterparty_Delete_UnusedRemovesItsAccounts_MissingReturnsFalse()
    {
        var db = await SeedCounterpartiesAsync();
        var service = CounterpartyService(db);

        Assert.True(await service.DeleteAsync(2));
        Assert.False(await db.Context.MarketCounterpartyAccounts.AnyAsync(a => a.CounterpartyId == 2));
        Assert.False(await service.DeleteAsync(999));
    }

    [Fact]
    public async Task Account_BlankCode_UnknownCounterparty_Rejected()
    {
        var db = await SeedCounterpartiesAsync();
        var service = AccountService(db);

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Account(1, "  ")));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Account(999)));
        Assert.Contains("não encontrada", ex.Message);
    }

    [Fact]
    public async Task Account_DuplicateCodeInSameCounterpartyRejected_AllowedInAnother()
    {
        var db = await SeedCounterpartiesAsync();
        var service = AccountService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Account(1, " c-001 ")));
        Assert.Contains("código", ex.Message);
        var other = await service.CreateAsync(Account(2, "C-001"));
        Assert.True(other.Id > 0);
    }

    [Fact]
    public async Task Account_Update_CannotMoveToAnotherCounterparty_NorDuplicateCode()
    {
        var db = await SeedCounterpartiesAsync();
        var service = AccountService(db);
        await service.CreateAsync(Account(1, "C-002"));
        db.Context.ChangeTracker.Clear();

        var move = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(1, Account(2, "C-001")));
        Assert.Contains("outra", move.Message);
        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(1, Account(1, "C-002")));
        db.Context.ChangeTracker.Clear();
        Assert.NotNull(await service.UpdateAsync(1, Account(1, "C-001")));
        db.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(999, Account(1, "X")));
    }

    [Theory]
    [InlineData("trade")]
    [InlineData("fx")]
    [InlineData("efp")]
    [InlineData("statement")]
    public async Task Account_Delete_RefusedWhenUsed_AllowedWhenFree(string table)
    {
        var db = await SeedCounterpartiesAsync();
        await UseAccountAsync(db, table, 1);
        var service = AccountService(db);

        await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(1));
        Assert.True(await service.DeleteAsync(2));
        Assert.False(await service.DeleteAsync(999));
    }

    [Fact]
    public async Task Account_NestedQuery_FiltersByCounterparty()
    {
        var db = await SeedCounterpartiesAsync();

        var ids = await AccountService(db).QueryByCounterparty(2).Select(a => a.Id).ToListAsync();

        Assert.Equal([2], ids);
    }
}
