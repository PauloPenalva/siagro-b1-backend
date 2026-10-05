using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>Natureza de devolução de compra de terceiro da filial (spec terceiro D3): de Saída e ativa.</summary>
public class BranchThirdPartyReturnUsageTests
{
    private static BranchService Service(UnitOfWork db) => new(db.Context, TaxTestServices.Config("STANDALONE"));

    private static async Task<int> UsageAsync(UnitOfWork db, UsageDirection direction, bool inactive = false)
    {
        var usage = new Usage { Name = "DEVOLUCAO DE COMPRA", Direction = direction, Inactive = inactive };
        db.Context.Usages.Add(usage);
        await db.SaveChangesAsync();
        return usage.Code;
    }

    private static Branch NewBranch(int? usageCode) => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101",
        IssuesNfe = true, TaxRegime = TaxRegime.Normal, StateCode = "SP", ThirdPartyPurchaseReturnUsageCode = usageCode,
    };

    [Theory]
    [InlineData(UsageDirection.Incoming, false)]
    [InlineData(UsageDirection.Outgoing, true)]
    public async Task Incoming_or_inactive_usage_is_refused(UsageDirection direction, bool inactive)
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await UsageAsync(db, direction, inactive);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(NewBranch(code)));

        Assert.Equal("A natureza de devolução de compra de terceiro precisa ser de Saída e ativa.", e.Message);
    }

    [Fact]
    public async Task Outgoing_active_usage_is_saved()
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await UsageAsync(db, UsageDirection.Outgoing);

        await Service(db).CreateAsync(NewBranch(code));

        Assert.Equal(code, (await db.Context.Branchs.AsNoTracking().SingleAsync()).ThirdPartyPurchaseReturnUsageCode);
    }

    [Fact]
    public async Task Empty_usage_is_accepted()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(NewBranch(null));

        Assert.Null((await db.Context.Branchs.AsNoTracking().SingleAsync()).ThirdPartyPurchaseReturnUsageCode);
    }

    [Fact]
    public async Task Update_validates_the_usage_too()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(NewBranch(null));
        db.Context.ChangeTracker.Clear();
        var code = await UsageAsync(db, UsageDirection.Incoming);
        db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync("01", NewBranch(code)));
    }
}
