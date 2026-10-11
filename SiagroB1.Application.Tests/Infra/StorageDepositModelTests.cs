using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

/// <summary>Remessa para depósito (spec 2026-10-10 §2): FKs Restrict e um retorno por romaneio.</summary>
public class StorageDepositModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void Storage_transaction_is_reconciled_at_most_once()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(StorageReturnReconciliation))!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(StorageReturnReconciliation.StorageTransactionKey)]));

        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData(typeof(StorageReturnReconciliation), typeof(PurchaseInvoice))]
    [InlineData(typeof(StorageReturnReconciliation), typeof(StorageTransaction))]
    [InlineData(typeof(SalesInvoice), typeof(ShipmentRelease))]
    public void Foreign_keys_are_restrict(Type dependent, Type principal)
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(dependent)!
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == principal);

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Role_defaults_to_none_in_the_database()
    {
        using var context = CreateContext();

        var role = context.Model.FindEntityType(typeof(UsageEffect))!
            .FindProperty(nameof(UsageEffect.StorageDepositRole))!;

        Assert.False(role.IsNullable);
    }
}
