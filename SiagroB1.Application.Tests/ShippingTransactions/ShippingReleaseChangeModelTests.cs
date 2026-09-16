using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// Mapeamento relacional do documento de troca de liberação (GAC-1177). Usa o provider
/// SqlServer só para materializar o modelo — nenhuma conexão é aberta.
/// </summary>
public class ShippingReleaseChangeModelTests
{
    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ShippingReleaseChange_IsMappedToExpectedTableAndColumns()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShippingReleaseChange));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPPING_RELEASE_CHANGES", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShippingReleaseChange.ShipmentLoadKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.OperationGroupKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.OriginalSalesStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.OriginalPurchaseStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.ReturnSalesStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.ReturnPurchaseStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.NewSalesStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.NewPurchaseStorageTransactionKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.SourceShipmentReleaseKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.TargetShipmentReleaseKey), props);
        Assert.Contains(nameof(ShippingReleaseChange.OriginalQuantity), props);
        Assert.Contains(nameof(ShippingReleaseChange.NewQuantity), props);
        Assert.Contains(nameof(ShippingReleaseChange.Reason), props);
    }

    [Fact]
    public void ShippingReleaseChange_HasIndexesOnShipmentLoadKeyAndOperationGroupKey()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShippingReleaseChange))!;

        Assert.Contains(entityType.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ShippingReleaseChange.ShipmentLoadKey)]));
        Assert.Contains(entityType.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ShippingReleaseChange.OperationGroupKey)]));
    }

    [Fact]
    public void ShippingReleaseChange_Reason_IsRequiredVarchar500()
    {
        using var context = ModelOnlyContext();

        var property = context.Model.FindEntityType(typeof(ShippingReleaseChange))!
            .FindProperty(nameof(ShippingReleaseChange.Reason))!;

        Assert.False(property.IsNullable);
        Assert.Equal("VARCHAR(500)", property.FindAnnotation("Relational:ColumnType")?.Value);
    }

    [Fact]
    public void ShippingReleaseChange_OperationGroupKey_IsNotAForeignKey()
    {
        // Correlaciona as linhas da MESMA chamada (troca com mais de um item ou inversão);
        // não há uma linha "dona" do grupo, então não é FK — só índice para consulta.
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShippingReleaseChange))!;
        var fkProps = entityType.GetForeignKeys().SelectMany(f => f.Properties).Select(p => p.Name);

        Assert.DoesNotContain(nameof(ShippingReleaseChange.OperationGroupKey), fkProps);
    }

    [Theory]
    [InlineData(nameof(ShippingReleaseChange.ShipmentLoadKey), typeof(ShipmentLoad))]
    [InlineData(nameof(ShippingReleaseChange.OriginalSalesStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.OriginalPurchaseStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.ReturnSalesStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.ReturnPurchaseStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.NewSalesStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.NewPurchaseStorageTransactionKey), typeof(StorageTransaction))]
    [InlineData(nameof(ShippingReleaseChange.SourceShipmentReleaseKey), typeof(ShipmentRelease))]
    [InlineData(nameof(ShippingReleaseChange.TargetShipmentReleaseKey), typeof(ShipmentRelease))]
    public void ShippingReleaseChange_ForeignKeys_PointToExpectedPrincipal_AsNoActionWithoutNavigation(
        string fkPropertyName, Type principalType)
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShippingReleaseChange))!;

        var fk = entityType.GetForeignKeys()
            .Single(f => f.Properties.Select(p => p.Name).SequenceEqual([fkPropertyName]));

        Assert.Equal(principalType, fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.Null(fk.PrincipalToDependent); // WithMany() sem coleção inversa
        Assert.Null(fk.DependentToPrincipal); // sem navigation property nesta entidade
    }

    [Theory]
    [InlineData(nameof(StorageTransaction.ReplacedByShippingReleaseChangeKey))]
    [InlineData(nameof(StorageTransaction.ShippingReleaseChangeKey))]
    public void StorageTransaction_HasForeignKeyToShippingReleaseChange_AsNoActionWithoutNavigation(
        string fkPropertyName)
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(StorageTransaction))!;

        var fk = entityType.GetForeignKeys()
            .Single(f => f.Properties.Select(p => p.Name).SequenceEqual([fkPropertyName]));

        Assert.Equal(typeof(ShippingReleaseChange), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.Null(fk.PrincipalToDependent);
        Assert.Null(fk.DependentToPrincipal); // sem navigation property nas duas colunas novas
    }
}
