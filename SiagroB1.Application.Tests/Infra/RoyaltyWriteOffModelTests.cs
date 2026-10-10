using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class RoyaltyWriteOffModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ItsId_is_unique_only_among_active_write_offs()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(RoyaltyWriteOff))!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(RoyaltyWriteOff.ItsId)]));

        Assert.True(index.IsUnique);
        Assert.Equal($"[Status] = {(int)RoyaltyWriteOffStatus.Active}", index.GetFilter());
    }

    [Fact]
    public void Line_weight_has_the_same_scale_as_the_net_weight()
    {
        using var context = CreateContext();

        var weight = context.Model.FindEntityType(typeof(RoyaltyWriteOffLine))!
            .FindProperty(nameof(RoyaltyWriteOffLine.Weight))!;

        Assert.Equal("DECIMAL(18,3)", weight.GetColumnType());
    }

    [Fact]
    public void Line_cannot_delete_its_storage_transaction()
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(typeof(RoyaltyWriteOffLine))!
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(StorageTransaction));

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }
}
