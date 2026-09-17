using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReleaseModelTests
{
    /// <summary>
    /// Usa o provider SqlServer só para materializar o modelo relacional; sem conexão. Com o
    /// provider InMemory (TestDb.CreateUnitOfWork()) não há metadado relacional e GetTableName()
    /// devolve o nome do tipo em vez do nome da tabela — ver TruckScaleCaptureModelTests.
    /// </summary>
    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void Distribution_line_is_mapped_with_unique_release_per_reconciliation_and_restrict_to_release()
    {
        using var db = ModelOnlyContext();
        var entity = db.Model.FindEntityType(typeof(WarehouseReconciliationRelease))!;

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASES", entity.GetTableName());

        var unique = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(
            new[] { nameof(WarehouseReconciliationRelease.WarehouseReconciliationKey), nameof(WarehouseReconciliationRelease.ShipmentReleaseKey) },
            unique.Properties.Select(p => p.Name).ToArray());

        var toReconciliation = entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(WarehouseReconciliation));
        Assert.Equal(DeleteBehavior.Cascade, toReconciliation.DeleteBehavior);

        var toRelease = entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(ShipmentRelease));
        Assert.Equal(DeleteBehavior.Restrict, toRelease.DeleteBehavior);
    }
}
